using Microsoft.AspNetCore.Mvc.Filters;

namespace Hastane.WebUI.Infrastructure;

// Adreste gizli bilgi (sıfırlama belirteci) taşıyan sayfalar: tarayıcı bu adresi hiçbir dış bağlantıya Referer olarak
// yollamasın, ara belleğe de koymasın. Genel ara katman başlığını (strict-origin-when-cross-origin) bu sayfada ezer.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoReferrerAttribute : ResultFilterAttribute
{
    public override void OnResultExecuting(ResultExecutingContext context)
    {
        var headers = context.HttpContext.Response.Headers;
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cache-Control"] = "no-store";
        base.OnResultExecuting(context);
    }
}
