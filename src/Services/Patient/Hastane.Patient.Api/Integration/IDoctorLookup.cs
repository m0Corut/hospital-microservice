namespace Hastane.Patient.Api.Integration;

public interface IDoctorLookup
{
    Task<LookupResult<DoctorRef>> GetPublicAsync(string doctorId, CancellationToken ct);
}
