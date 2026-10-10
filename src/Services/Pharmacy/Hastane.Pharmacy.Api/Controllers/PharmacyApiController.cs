using Hastane.Pharmacy.Api.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Pharmacy.Api.Controllers;

// Controller'ın tek işi: HTTP isteğini komut/sorguya çevirip ISender.Send ile yola koymak, dönen sonucu HTTP koduna çevirmek.
// İş mantığı (kural, veritabanı, transaction) burada YOK; hepsi Features/ altındaki handler'larda.
[ApiController]
public abstract class PharmacyApiController(ISender sender) : ControllerBase
{
    protected ISender Sender { get; } = sender;

    protected IActionResult ToActionResult<T>(OperationResult<T> result)
        => result.IsSuccess ? Ok(result.Value) : Failure(result);

    // 204 dönen komutlar (silme).
    protected IActionResult ToNoContent<T>(OperationResult<T> result)
        => result.IsSuccess ? NoContent() : Failure(result);

    protected IActionResult Failure<T>(OperationResult<T> result)
    {
        var (status, title) = result.Status switch
        {
            OperationStatus.NotFound => (StatusCodes.Status404NotFound, "Bulunamadı"),
            OperationStatus.Conflict => (StatusCodes.Status409Conflict, "Çakışma"),
            OperationStatus.Forbidden => (StatusCodes.Status403Forbidden, "Erişim reddedildi"),
            OperationStatus.ServiceUnavailable => (StatusCodes.Status503ServiceUnavailable, "Servis kullanılamıyor"),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen durum"),
        };

        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title, detail: result.Error);

        // 409'da hangi kalemlerin neden karşılanamadığı gibi ek bilgi.
        if (result.Details is not null) problem.Extensions["items"] = result.Details;

        return new ObjectResult(problem) { StatusCode = status };
    }
}
