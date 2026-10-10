using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;
using Microsoft.AspNetCore.WebUtilities;

namespace Hastane.WebUI.Clients;

// Admin istemcileri için ortak yardımcı: boş değerleri atarak sorgu dizesi kurar (Uri.EscapeDataString benzeri kodlama).
internal static class AdminQuery
{
    public static string Build(string path, params (string Key, string? Value)[] parameters)
    {
        var query = parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .ToDictionary(p => p.Key, p => (string?)p.Value!.Trim());
        return query.Count == 0 ? path : QueryHelpers.AddQueryString(path, query);
    }
}

public interface IAdminBranchClient
{
    Task<ApiResult<List<AdminBranchApiModel>>> GetAllAsync(ApiFailures handle, CancellationToken ct);

    Task<ApiResult<AdminBranchApiModel>> GetAsync(string id, CancellationToken ct);

    Task<ApiResult<AdminBranchApiModel>> CreateAsync(AdminBranchWriteBody body, CancellationToken ct);

    Task<ApiResult> UpdateAsync(string id, AdminBranchWriteBody body, CancellationToken ct);

    Task<ApiResult> DeleteAsync(string id, CancellationToken ct);
}

public sealed class AdminBranchClient(IGatewayClient gateway) : IAdminBranchClient
{
    public Task<ApiResult<List<AdminBranchApiModel>>> GetAllAsync(ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<List<AdminBranchApiModel>>("/gateway/branches", handle, ct);

    public Task<ApiResult<AdminBranchApiModel>> GetAsync(string id, CancellationToken ct)
        => gateway.GetAsync<AdminBranchApiModel>($"/gateway/branches/{Uri.EscapeDataString(id)}", ct: ct);

    public Task<ApiResult<AdminBranchApiModel>> CreateAsync(AdminBranchWriteBody body, CancellationToken ct)
        => gateway.PostAsync<AdminBranchApiModel>("/gateway/branches", body, ct: ct);

    public Task<ApiResult> UpdateAsync(string id, AdminBranchWriteBody body, CancellationToken ct)
        => gateway.PutAsync($"/gateway/branches/{Uri.EscapeDataString(id)}", body, ct: ct);

    public Task<ApiResult> DeleteAsync(string id, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/branches/{Uri.EscapeDataString(id)}", ct: ct);
}
