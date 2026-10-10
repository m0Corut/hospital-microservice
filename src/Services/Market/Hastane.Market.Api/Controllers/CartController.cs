using Hastane.Market.Api.Dtos;
using Hastane.Market.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Market.Api.Controllers;

// Sepet kullanıcıya özeldir ve anahtarı TOKEN'daki sub'dan türer: başkasının sepetine gitmenin yolu yok (IDOR yok).
[ApiController]
[Route("api/cart")]
public class CartController(ICartService service, ICurrentUser user) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = HastaneRoles.Patient)]
    public async Task<ActionResult<CartResultDto>> Get(CancellationToken ct)
        => ToActionResult(await service.GetAsync(user.UserId!, ct));

    // PUT = "bu ürünün adedi şu olsun" (idempotent). Adet 0 ise ürün sepetten çıkar.
    [HttpPut("items/{productId:guid}")]
    [Authorize(Roles = HastaneRoles.Patient)]
    public async Task<ActionResult<CartResultDto>> SetItem(Guid productId, SetCartItemDto dto, CancellationToken ct)
        => ToActionResult(await service.SetItemAsync(user.UserId!, productId, dto.Quantity, ct));

    [HttpDelete]
    [Authorize(Roles = HastaneRoles.Patient)]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var result = await service.ClearAsync(user.UserId!, ct);
        return result.Status == OperationStatus.Ok ? NoContent() : Unavailable(result.Error);
    }

    private ActionResult<CartResultDto> ToActionResult(OperationResult<CartResultDto> result) => result.Status switch
    {
        OperationStatus.Ok => Ok(result.Value),
        OperationStatus.NotFound => NotFound(new ProblemDetails { Status = 404, Title = "Bulunamadı", Detail = result.Error }),
        OperationStatus.Validation => BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = result.Error }),
        OperationStatus.Conflict => Conflict(new ProblemDetails { Status = 409, Title = "Stok yetersiz", Detail = result.Error }),
        OperationStatus.ServiceUnavailable => Unavailable(result.Error),
        _ => StatusCode(500),
    };

    // Redis kapalıyken sepet çalışmaz: 503. (Ürün uçları etkilenmez.)
    private ObjectResult Unavailable(string? detail)
        => StatusCode(StatusCodes.Status503ServiceUnavailable,
            new ProblemDetails { Status = 503, Title = "Servis kullanılamıyor", Detail = detail });
}
