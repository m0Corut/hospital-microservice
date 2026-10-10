namespace Hastane.Chat.Api.Integration;

// "Bu hasta ile bu doktorun randevusu var mı?" sorusu. Appointment listeyi token kapsamına göre zaten süzer
// (hasta yalnız kendi randevusunu, doktor yalnız kendi hastalarınınkini görür); bu yüzden boş olmayan liste = ilişki var.
public interface IAppointmentLookup
{
    Task<LookupResult<List<AppointmentRef>>> ListAsync(string? doctorId, string? patientId, CancellationToken ct);
}

public interface IDoctorLookup
{
    Task<LookupResult<PublicDoctorInfo>> GetPublicAsync(string doctorId, CancellationToken ct);
}

public interface IUserLookup
{
    Task<LookupResult<List<UserInfo>>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken ct);
}

public class AppointmentLookup(HttpClient http, IHttpContextAccessor accessor, ILogger<AppointmentLookup> logger) : IAppointmentLookup
{
    public Task<LookupResult<List<AppointmentRef>>> ListAsync(string? doctorId, string? patientId, CancellationToken ct)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(doctorId)) query.Add($"doctorId={Uri.EscapeDataString(doctorId)}");
        if (!string.IsNullOrWhiteSpace(patientId)) query.Add($"patientId={Uri.EscapeDataString(patientId)}");
        return DownstreamClient.GetAsync<List<AppointmentRef>>(http, "/api/appointments?" + string.Join('&', query), accessor, logger, ct);
    }
}

public class DoctorLookup(HttpClient http, IHttpContextAccessor accessor, ILogger<DoctorLookup> logger) : IDoctorLookup
{
    public Task<LookupResult<PublicDoctorInfo>> GetPublicAsync(string doctorId, CancellationToken ct)
        => DownstreamClient.GetAsync<PublicDoctorInfo>(http, $"/api/doctors/{Uri.EscapeDataString(doctorId)}/public", accessor, logger, ct);
}

public class UserLookup(HttpClient http, IHttpContextAccessor accessor, ILogger<UserLookup> logger) : IUserLookup
{
    public Task<LookupResult<List<UserInfo>>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken ct)
        => DownstreamClient.GetAsync<List<UserInfo>>(http, "/api/users?ids=" + Uri.EscapeDataString(string.Join(',', ids)), accessor, logger, ct);
}
