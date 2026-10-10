namespace Hastane.Patient.Api.Integration;

public class AppointmentLookup(HttpClient http, IHttpContextAccessor accessor, ILogger<AppointmentLookup> logger) : IAppointmentLookup
{
    public Task<LookupResult<List<AppointmentRef>>> GetForPatientAsync(string patientId, CancellationToken ct)
        => DownstreamClient.GetAsync<List<AppointmentRef>>(
            http, $"/api/appointments?patientId={Uri.EscapeDataString(patientId)}", accessor, logger, ct);
}
