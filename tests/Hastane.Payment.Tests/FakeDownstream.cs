using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Hastane.Payment.Tests;

public enum DownstreamMode
{
    Normal,
    Down,     // bağlantı reddedildi
    Timeout,  // yanıt hiç gelmez (HttpClient zaman aşımı devreye girer)
    Error500,
    Forbidden403,
    BadJson,
}

public sealed record FakeAppointment(string PatientId, string DoctorId, string Status);

public sealed record FakeLocation(decimal? HourlyFee, string Currency);

public sealed record DownstreamCall(string Path, string? Authorization);

// Appointment ve Doctor servislerinin sahte hali: gerçek ağ yok, Docker yok. HttpMessageHandler olarak
// payment'ın typed HttpClient'larının altına takılır. Sahiplik kararını GERÇEK Appointment gibi token'a bakarak verir:
// Admin hepsini görür, Patient yalnız kendi randevusunu (başkasınınki 404), Doctor yalnız kendi doktor_id'sini.
// Testler seri çalışır; durum statik ve her testin kendi (benzersiz) kimlikleri vardır.
public static class FakeDownstream
{
    private static readonly ConcurrentDictionary<Guid, FakeAppointment> Appointments = new();
    private static readonly ConcurrentDictionary<string, FakeLocation[]> Doctors = new();
    private static readonly ConcurrentQueue<DownstreamCall> CallLog = new();

    public static DownstreamMode AppointmentMode { get; set; } = DownstreamMode.Normal;
    public static DownstreamMode DoctorMode { get; set; } = DownstreamMode.Normal;

    public static IReadOnlyList<DownstreamCall> Calls => CallLog.ToArray();

    public static int CallCount(string pathPrefix) => CallLog.Count(c => c.Path.StartsWith(pathPrefix, StringComparison.Ordinal));

    public static void Reset()
    {
        AppointmentMode = DownstreamMode.Normal;
        DoctorMode = DownstreamMode.Normal;
    }

    public static void RegisterAppointment(Guid id, string patientId, string doctorId, string status = "Confirmed")
        => Appointments[id] = new FakeAppointment(patientId, doctorId, status);

    public static void RegisterDoctor(string doctorId, params FakeLocation[] locations)
        => Doctors[doctorId] = locations;

    public static HttpMessageHandler CreateHandler() => new Handler();

    private sealed class Handler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            CallLog.Enqueue(new DownstreamCall(path, request.Headers.Authorization?.ToString()));

            var isAppointment = path.StartsWith("/api/appointments/", StringComparison.Ordinal);
            var mode = isAppointment ? AppointmentMode : DoctorMode;
            switch (mode)
            {
                case DownstreamMode.Down: throw new HttpRequestException("bağlantı reddedildi (test)");
                case DownstreamMode.Timeout:
                    await Task.Delay(Timeout.Infinite, ct);
                    break;
                case DownstreamMode.Error500: return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                case DownstreamMode.Forbidden403: return new HttpResponseMessage(HttpStatusCode.Forbidden);
                case DownstreamMode.BadJson: return Json("<html>not json");
            }

            var caller = Caller.From(request.Headers.Authorization?.Parameter);
            var id = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);

            if (isAppointment)
            {
                if (!Guid.TryParse(id, out var guid) || !Appointments.TryGetValue(guid, out var a)) return Status(HttpStatusCode.NotFound);
                var visible = caller.IsAdmin || (caller.IsPatient && caller.Sub == a.PatientId) || (caller.IsDoctor && caller.DoctorId == a.DoctorId);
                if (!visible) return Status(HttpStatusCode.NotFound);
                return Json(JsonSerializer.Serialize(new { id = guid, doctorId = a.DoctorId, patientId = a.PatientId, status = a.Status }));
            }

            if (caller.Sub is null) return Status(HttpStatusCode.Unauthorized);
            if (!Doctors.TryGetValue(id, out var locations)) return Status(HttpStatusCode.NotFound);
            // Doctor'ın türetme kuralı (DoctorDerived): >0 ücretli ilk konumun birimi, o birimdeki en düşük ücret.
            var priced = locations.Where(l => l.HourlyFee is > 0).ToList();
            var currency = priced.Count == 0 ? null : priced[0].Currency;
            decimal? minFee = priced.Count == 0 ? null : priced.Where(l => l.Currency == currency).Min(l => l.HourlyFee);
            return Json(JsonSerializer.Serialize(new
            {
                id,
                clinicLocations = locations.Select(l => new { hourlyFee = l.HourlyFee, currency = l.Currency }),
                minFee,
                currency,
            }));
        }

        private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
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
