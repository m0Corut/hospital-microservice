using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Hastane.Queue.Tests;

public enum DownstreamMode
{
    Normal,
    Down,     // bağlantı reddedildi
    Error500,
}

public sealed record FakeAppointment(string PatientId, string DoctorId, string Status, DateTime AppointmentDateUtc);

public sealed record DownstreamCall(string Path, string? Authorization);

// Appointment servisinin sahte hali: gerçek ağ yok. Sahiplik kararını GERÇEK Appointment gibi token'a bakarak verir:
// Admin hepsini görür, Patient yalnız kendi randevusunu (başkasınınki 404), Doctor yalnız kendi doktor_id'sini.
// Testler seri çalışır; durum statik ve her testin kendi (benzersiz) kimlikleri vardır.
public static class FakeDownstream
{
    private static readonly ConcurrentDictionary<Guid, FakeAppointment> Appointments = new();
    private static readonly ConcurrentQueue<DownstreamCall> CallLog = new();

    public static DownstreamMode Mode { get; set; } = DownstreamMode.Normal;

    public static IReadOnlyList<DownstreamCall> Calls => CallLog.ToArray();

    public static void Reset() => Mode = DownstreamMode.Normal;

    public static void RegisterAppointment(Guid id, string patientId, string doctorId, string status, DateTime appointmentDateUtc)
        => Appointments[id] = new FakeAppointment(patientId, doctorId, status, appointmentDateUtc);

    public static HttpMessageHandler CreateHandler() => new Handler();

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            CallLog.Enqueue(new DownstreamCall(path, request.Headers.Authorization?.ToString()));

            switch (Mode)
            {
                case DownstreamMode.Down: throw new HttpRequestException("bağlantı reddedildi (test)");
                case DownstreamMode.Error500: return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            var caller = Caller.From(request.Headers.Authorization?.Parameter);
            var id = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
            if (!Guid.TryParse(id, out var guid) || !Appointments.TryGetValue(guid, out var a))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var visible = caller.IsAdmin || (caller.IsPatient && caller.Sub == a.PatientId) || (caller.IsDoctor && caller.DoctorId == a.DoctorId);
            if (!visible) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var body = JsonSerializer.Serialize(new
            {
                id = guid,
                doctorId = a.DoctorId,
                patientId = a.PatientId,
                status = a.Status,
                appointmentDate = a.AppointmentDateUtc,
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed record Caller(string? Sub, string[] Roles, string? DoctorId)
    {
        public bool IsAdmin => Roles.Contains("Admin");
        public bool IsPatient => Roles.Contains("Patient");
        public bool IsDoctor => Roles.Contains("Doctor") && DoctorId is not null;

        // İmza doğrulamaz (sahte servis); yalnız yük kısmını okur.
        public static Caller From(string? token)
        {
            if (string.IsNullOrEmpty(token)) return new Caller(null, [], null);
            var parts = token.Split('.');
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = doc.RootElement;
            var roles = root.TryGetProperty("role", out var r)
                ? (r.ValueKind == JsonValueKind.Array ? r.EnumerateArray().Select(x => x.GetString()!).ToArray() : [r.GetString()!])
                : [];
            return new Caller(
                root.TryGetProperty("sub", out var s) ? s.GetString() : null,
                roles,
                root.TryGetProperty("doctor_id", out var d) ? d.GetString() : null);
        }
    }
}
