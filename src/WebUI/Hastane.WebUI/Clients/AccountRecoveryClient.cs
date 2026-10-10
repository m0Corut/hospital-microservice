using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api;

namespace Hastane.WebUI.Clients;

// Anonim hesap akışları: şifre sıfırlama ve doktor başvurusu. Yollar yalnız burada.
public interface IAccountRecoveryClient
{
    // 202 (her zaman aynı gövde) | 400 (e-posta biçimi). Kullanıcı var/yok ayrımı yoktur.
    Task<ApiResult> ForgotPasswordAsync(string email, CancellationToken ct);

    // 204 | 400 (geçersiz/süresi dolmuş bağlantı ya da zayıf şifre: Errors dolu).
    Task<ApiResult> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct);

    // 201 | 400 | 409 (e-posta kayıtlı).
    Task<ApiResult<DoctorApplicationApiModel>> RegisterDoctorAsync(
        string firstName, string lastName, string email, string password, string? branchId, string licenseNumber, string? note, CancellationToken ct);

    // 200 | 404 (başvuru yok: çağırana döner).
    Task<ApiResult<DoctorApplicationApiModel>> GetMyDoctorApplicationAsync(CancellationToken ct);
}

public sealed class AccountRecoveryClient(IGatewayClient gateway) : IAccountRecoveryClient
{
    // Anonim akışlarda 401/403/404 "oturum/sayfa" hatası değil, formun ele alacağı sonuçtur (kullanıcı var/yok sızmasın).
    // Servis kapalıysa (503) ortak hata sayfası gösterilir: "gönderildi" demek yalan olurdu.
    private const ApiFailures Handled = ApiFailures.Unauthorized | ApiFailures.Forbidden | ApiFailures.NotFound;

    public Task<ApiResult> ForgotPasswordAsync(string email, CancellationToken ct)
        => gateway.PostAsync("/gateway/auth/forgot-password", new { email }, Handled, ct);

    public Task<ApiResult> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken ct)
        => gateway.PostAsync("/gateway/auth/reset-password", new { email, token, newPassword }, Handled, ct);

    public Task<ApiResult<DoctorApplicationApiModel>> RegisterDoctorAsync(
        string firstName, string lastName, string email, string password, string? branchId, string licenseNumber, string? note, CancellationToken ct)
        => gateway.PostAsync<DoctorApplicationApiModel>("/gateway/auth/register-doctor",
            new { firstName, lastName, email, password, branchId, licenseNumber, note }, ct: ct);

    public Task<ApiResult<DoctorApplicationApiModel>> GetMyDoctorApplicationAsync(CancellationToken ct)
        => gateway.GetAsync<DoctorApplicationApiModel>("/gateway/auth/me/doctor-application", ApiFailures.NotFound, ct);
}
