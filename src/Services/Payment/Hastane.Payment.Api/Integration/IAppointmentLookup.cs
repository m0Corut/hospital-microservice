namespace Hastane.Payment.Api.Integration;

public interface IAppointmentLookup
{
    Task<LookupResult<AppointmentInfo>> GetAsync(Guid appointmentId, CancellationToken ct);
}
