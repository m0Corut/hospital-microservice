namespace Hastane.Queue.Api.Integration;

public class AppointmentLookup(HttpClient http, IHttpContextAccessor accessor, ILogger<AppointmentLookup> logger) : IAppointmentLookup
{
    public Task<LookupResult<AppointmentInfo>> GetAsync(Guid appointmentId, CancellationToken ct)
        => DownstreamClient.GetAsync<AppointmentInfo>(http, $"/api/appointments/{appointmentId:D}", accessor, logger, ct);
}

