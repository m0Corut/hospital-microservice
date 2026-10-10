using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Pharmacy.Api.Behaviors;

// RequestValidationException'ı 400 + ValidationProblemDetails'e çevirir (MVC'nin kendi model doğrulamasıyla aynı biçim).
public sealed class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not RequestValidationException validation) return false;

        var errors = validation.Failures
            .GroupBy(f => f.Property)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Message).ToArray());

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Geçersiz istek",
            Detail = "Bir ya da daha fazla alan geçersiz.",
        };

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
        return true;
    }
}
