namespace Hastane.Patient.Api.Integration;

// Yalnız "bu doktor var mı" sorusu: herkese açık profil ucu yeterli (telefon/e-posta gerekmez).
public class DoctorLookup(HttpClient http, IHttpContextAccessor accessor, ILogger<DoctorLookup> logger) : IDoctorLookup
{
    public Task<LookupResult<DoctorRef>> GetPublicAsync(string doctorId, CancellationToken ct)
        => DownstreamClient.GetAsync<DoctorRef>(http, $"/api/doctors/{Uri.EscapeDataString(doctorId)}/public", accessor, logger, ct);
}
