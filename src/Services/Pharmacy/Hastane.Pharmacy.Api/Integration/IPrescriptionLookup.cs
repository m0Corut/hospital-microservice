namespace Hastane.Pharmacy.Api.Integration;

public interface IPrescriptionLookup
{
    Task<LookupResult<PrescriptionInfo>> GetAsync(Guid prescriptionId, CancellationToken ct);
}
