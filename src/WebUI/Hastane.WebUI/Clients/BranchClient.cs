using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api;

namespace Hastane.WebUI.Clients;

public interface IBranchClient
{
    Task<ApiResult<List<BranchApiModel>>> GetAllAsync(ApiFailures handle, CancellationToken ct);
}

public sealed class BranchClient(IGatewayClient gateway) : IBranchClient
{
    public Task<ApiResult<List<BranchApiModel>>> GetAllAsync(ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<List<BranchApiModel>>("/gateway/branches", handle, ct);
}
