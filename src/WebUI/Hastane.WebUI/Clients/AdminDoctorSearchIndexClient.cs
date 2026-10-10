using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.Clients;

// Doktor arama dizini (Elasticsearch) bakımı: yalnız Admin. Dizin türetilmiş bir kopyadır; kaynak gerçek MongoDB'dir.
public interface IAdminDoctorSearchIndexClient
{
    // GET doctors/search-index/status -> { enabled, reachable, index, indexedCount, mongoCount, inSync }
    Task<ApiResult<DoctorSearchIndexStatusApiModel>> StatusAsync(CancellationToken ct);

    // POST doctors/search-index/rebuild -> 200 { indexed } | 409 (dizin kapalı) | 503 (dizine ulaşılamadı)
    Task<ApiResult<DoctorSearchIndexRebuildApiModel>> RebuildAsync(CancellationToken ct);
}

public sealed class AdminDoctorSearchIndexClient(IGatewayClient gateway) : IAdminDoctorSearchIndexClient
{
    public Task<ApiResult<DoctorSearchIndexStatusApiModel>> StatusAsync(CancellationToken ct)
        => gateway.GetAsync<DoctorSearchIndexStatusApiModel>("/gateway/doctors/search-index/status", ApiFailures.Unavailable, ct);

    public Task<ApiResult<DoctorSearchIndexRebuildApiModel>> RebuildAsync(CancellationToken ct)
        => gateway.PostAsync<DoctorSearchIndexRebuildApiModel>("/gateway/doctors/search-index/rebuild", null, ApiFailures.Unavailable, ct);
}
