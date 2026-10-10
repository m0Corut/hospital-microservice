using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Hastane.Patient.Tests;

public enum DownstreamMode
{
    Normal,
    Down,     // bağlantı reddedildi
    Timeout,  // yanıt hiç gelmez (HttpClient zaman aşımı devreye girer)
    Error500,
}

public sealed record DownstreamCall(string PathAndQuery, string? Authorization);

// Appointment ve Doctor servislerinin sahte hali: gerçek ağ yok. Appointment listesini GERÇEK servis gibi token'a bakarak süzer:
// Admin hepsini, Doctor yalnız kendi doctor_id'sine ait randevuları, Patient yalnız kendi randevularını görür.
// Testler seri çalışır; durum statik ve her testin kendi (benzersiz) kimlikleri vardır.
public static class FakeDownstream
{
    private sealed record Appt(Guid Id, string PatientId, string DoctorId);

    private static readonly ConcurrentBag<Appt> Appointments = [];
    private static readonly ConcurrentDictionary<string, bool> Doctors = new();
    private static readonly ConcurrentQueue<DownstreamCall> CallLog = new();

    public static DownstreamMode AppointmentMode { get; set; } = DownstreamMode.Normal;
    public static DownstreamMode DoctorMode { get; set; } = DownstreamMode.Normal;

    public static IReadOnlyList<DownstreamCall> Calls => CallLog.ToArray();

    public static int CallCount(string pathPrefix) => CallLog.Count(c => c.PathAndQuery.StartsWith(pathPrefix, StringComparison.Ordinal));

    public static void Reset()
    {
        AppointmentMode = DownstreamMode.Normal;
        DoctorMode = DownstreamMode.Normal;
    }

    public static void RegisterAppointment(string patientId, string doctorId) => Appointments.Add(new Appt(Guid.CreateVersion7(), patientId, doctorId));

    public static void RegisterDoctor(string doctorId) => Doctors[doctorId] = true;

    public static HttpMessageHandler CreateHandler() => new Handler();

    private sealed class Handler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            CallLog.Enqueue(new DownstreamCall(uri.PathAndQuery, request.Headers.Authorization?.ToString()));

            var isAppointment = uri.AbsolutePath.StartsWith("/api/appointments", StringComparison.Ordinal);
            switch (isAppointment ? AppointmentMode : DoctorMode)
            {
                case DownstreamMode.Down: throw new HttpRequestException("bağlantı reddedildi (test)");
                case DownstreamMode.Timeout:
                    await Task.Delay(Timeout.Infinite, ct);
                    break;
                case DownstreamMode.Error500: return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            if (isAppointment)
            {
                var caller = Caller.From(request.Headers.Authorization?.Parameter);
                if (caller.Sub is null) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                var patientId = System.Web.HttpUtility.ParseQueryString(uri.Query)["patientId"];
                var visible = Appointments.Where(a =>
                    (patientId is null || a.PatientId == patientId)
                    && (caller.IsAdmin
                        || (caller.IsPatient && a.PatientId == caller.Sub)
                        || (caller.IsDoctor && a.DoctorId == caller.DoctorId)));
                return Json(JsonSerializer.Serialize(visible.Select(a => new { id = a.Id, patientId = a.PatientId, doctorId = a.DoctorId })));
            }

            // GET /api/doctors/{id}/public (anonim uç)
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var doctorId = Uri.UnescapeDataString(segments[2]);
            return Doctors.ContainsKey(doctorId)
                ? Json(JsonSerializer.Serialize(new { id = doctorId, fullName = "Dr. Test" }))
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

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
            var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
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
