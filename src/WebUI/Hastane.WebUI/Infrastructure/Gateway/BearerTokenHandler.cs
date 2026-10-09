using System.Net.Http.Headers;
using Hastane.WebUI.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;

namespace Hastane.WebUI.Infrastructure.Gateway;

// Giden her API isteğine, oturum açıksa çerezdeki access token'ı Authorization: Bearer olarak ekler.
// Token tarayıcıya hiç gitmez: sunucu çerezden (şifreli) okur ve Gateway'e kendisi iletir.
public sealed class BearerTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var context = accessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated == true)
        {
            var token = await context.GetTokenAsync(AuthConstants.CookieScheme, AuthConstants.AccessTokenName);
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, ct);
    }
}
