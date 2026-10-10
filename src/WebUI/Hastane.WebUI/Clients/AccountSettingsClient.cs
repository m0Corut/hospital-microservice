using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api;

namespace Hastane.WebUI.Clients;

// Giriş yapmış kullanıcının kendi hesabı (tüm roller): kimlik token'dan gelir, gövdede Id yok.
public interface IAccountSettingsClient
{
    Task<ApiResult<UserSummaryApiModel>> GetMeAsync(CancellationToken ct);

    Task<ApiResult<UserSummaryApiModel>> UpdateProfileAsync(string firstName, string lastName, CancellationToken ct);

    // 204 | 400 (mevcut şifre yanlış / kural ihlali) | 423 (hesap kilitlendi).
    Task<ApiResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct);
}

public sealed class AccountSettingsClient(IGatewayClient gateway) : IAccountSettingsClient
{
    public Task<ApiResult<UserSummaryApiModel>> GetMeAsync(CancellationToken ct)
        => gateway.GetAsync<UserSummaryApiModel>("/gateway/auth/me", ct: ct);

    public Task<ApiResult<UserSummaryApiModel>> UpdateProfileAsync(string firstName, string lastName, CancellationToken ct)
        => gateway.PutAsync<UserSummaryApiModel>("/gateway/auth/me", new { firstName, lastName }, ct: ct);

    public Task<ApiResult> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct)
        => gateway.PostAsync("/gateway/auth/change-password", new { currentPassword, newPassword }, ct: ct);
}
