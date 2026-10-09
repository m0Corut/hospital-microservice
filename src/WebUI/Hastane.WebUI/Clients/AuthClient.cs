using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api;

namespace Hastane.WebUI.Clients;

public interface IAuthClient
{
    Task<ApiResult<LoginApiResult>> LoginAsync(string email, string password, CancellationToken ct);

    Task<ApiResult<UserSummaryApiModel>> RegisterAsync(string firstName, string lastName, string email, string password, CancellationToken ct);
}

// Alan istemcisi deseni: bir sınıf = bir servisin yolları. Yollar yalnız burada yazılır; controller yol bilmez.
public sealed class AuthClient(IGatewayClient gateway) : IAuthClient
{
    // Yanlış şifre 401 döner; bu bir "oturum süresi doldu" değil, formun ele alacağı sonuçtur → Unauthorized'ı biz alırız.
    public Task<ApiResult<LoginApiResult>> LoginAsync(string email, string password, CancellationToken ct)
        => gateway.PostAsync<LoginApiResult>("/gateway/auth/login", new { email, password }, ApiFailures.Unauthorized, ct);

    public Task<ApiResult<UserSummaryApiModel>> RegisterAsync(string firstName, string lastName, string email, string password, CancellationToken ct)
        => gateway.PostAsync<UserSummaryApiModel>("/gateway/auth/register", new { firstName, lastName, email, password }, ApiFailures.None, ct);
}
