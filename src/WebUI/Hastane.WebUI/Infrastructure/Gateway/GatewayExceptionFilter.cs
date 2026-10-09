using Hastane.WebUI.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hastane.WebUI.Infrastructure.Gateway;

// GatewayException'ı tek yerde kullanıcı davranışına çevirir:
//   401 → oturum kapat + girişe yönlendir (returnUrl ile)   403 → "Yetkiniz yok" sayfası
//   404 → "Bulunamadı" sayfası                              servis kapalı → "Servis şu an yanıt vermiyor"
// Durum kodu boş gövdeyle döner; UseStatusCodePagesWithReExecute /Error/{kod} sayfasını çizer.
public sealed class GatewayExceptionFilter : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is not GatewayException ex) return;

        context.ExceptionHandled = true;
        var http = context.HttpContext;

        switch (ex.Kind)
        {
            case ApiResultKind.Unauthorized:
                // Token API tarafında reddedildi (süre doldu / geçersiz): çerez de geçersizdir, sil.
                await http.SignOutAsync(AuthConstants.CookieScheme);
                var returnUrl = HttpMethods.IsGet(http.Request.Method)
                    ? $"{http.Request.Path}{http.Request.QueryString}"
                    : null;
                context.Result = new RedirectToActionResult("Login", "Account", new { area = "", returnUrl });
                break;
            case ApiResultKind.Forbidden:
                context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
                break;
            case ApiResultKind.NotFound:
                context.Result = new StatusCodeResult(StatusCodes.Status404NotFound);
                break;
            default:
                context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
                break;
        }
    }
}
