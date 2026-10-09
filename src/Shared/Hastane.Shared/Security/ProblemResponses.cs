using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Shared.Security;

// 401/403 cevapları ProblemDetails JSON'u olsun: gövdesiz boş cevap yerine istemcinin okuyabileceği biçim.
public static class ProblemResponses
{
    public static Task WriteUnauthorizedAsync(HttpContext context, bool invalidToken = false)
    {
        // RFC 6750: token hiç yoksa yalnız "Bearer"; verilmiş ama geçersizse error="invalid_token".
        context.Response.Headers.WWWAuthenticate = invalidToken ? "Bearer error=\"invalid_token\"" : "Bearer";
        return WriteAsync(context, StatusCodes.Status401Unauthorized, "Kimlik doğrulanamadı",
            "Geçerli bir erişim token'ı gerekli.");
    }

    public static Task WriteForbiddenAsync(HttpContext context)
        => WriteAsync(context, StatusCodes.Status403Forbidden, "Yetkiniz yok",
            "Bu işlem için gerekli role sahip değilsiniz.");

    private static Task WriteAsync(HttpContext context, int status, string title, string detail)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}
