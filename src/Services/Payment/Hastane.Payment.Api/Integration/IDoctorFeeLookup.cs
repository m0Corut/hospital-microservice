namespace Hastane.Payment.Api.Integration;

public interface IDoctorFeeLookup
{
    Task<LookupResult<DoctorFee>> GetFeeAsync(string doctorId, CancellationToken ct);
}
