namespace Hastane.Pharmacy.Api.Integration;

public class PrescriptionLookup(HttpClient http, IHttpContextAccessor accessor, ILogger<PrescriptionLookup> logger) : IPrescriptionLookup
{
    public Task<LookupResult<PrescriptionInfo>> GetAsync(Guid prescriptionId, CancellationToken ct)
        => DownstreamClient.GetAsync<PrescriptionInfo>(http, $"/api/prescriptions/{prescriptionId:D}", accessor, logger, ct);
}
