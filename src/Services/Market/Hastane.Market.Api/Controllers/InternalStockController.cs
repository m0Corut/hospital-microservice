using Hastane.Market.Api.Dtos;
using Hastane.Market.Api.Security;
using Hastane.Market.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Market.Api.Controllers;

// Servisten servise uçlar (Order çağırır): JWT değil X-Internal-Key. Gateway bu yolları dışarı AÇMAZ.
// [AllowAnonymous] "JWT istemez" demektir; kapı InternalApiKey filtresidir (anahtar yoksa/yanlışsa 401).
[ApiController]
[Route("api/internal/stock")]
public class InternalStockController(IStockService service) : ControllerBase
{
    // Stok yeterliyse HEPSİNİ atomik düşer; değilse hiçbirini düşmez ve 409 {productId, reason, requested, available}.
    // Aynı orderId ikinci kez gelirse 200 (replayed=true), stok tekrar düşmez.
    [HttpPost("reserve")]
    [AllowAnonymous]
    [InternalApiKey]
    public async Task<ActionResult<ReservationResultDto>> Reserve(ReserveStockDto dto, CancellationToken ct)
    {
        var result = await service.ReserveAsync(dto, ct);
        switch (result.Outcome)
        {
            case ReserveOutcome.Reserved:
            case ReserveOutcome.Replayed:
                return Ok(result.Reservation);
            case ReserveOutcome.Invalid:
                return BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = result.Error });
            default:
                var problem = new ProblemDetails { Status = 409, Title = "Stok yetersiz", Detail = result.Error };
                problem.Extensions["productId"] = result.Conflict!.ProductId;
                problem.Extensions["reason"] = result.Conflict.Reason;
                problem.Extensions["requested"] = result.Conflict.Requested;
                problem.Extensions["available"] = result.Conflict.Available;
                return Conflict(problem);
        }
    }

    // İade: yalnız rezerve edilmiş miktarı geri koyar ve bir kez. Tekrarında ya da rezervasyon yoksa 200 (stok değişmez).
    [HttpPost("release")]
    [AllowAnonymous]
    [InternalApiKey]
    public async Task<ActionResult<ReservationResultDto>> Release(ReleaseStockDto dto, CancellationToken ct)
    {
        var result = await service.ReleaseAsync(dto.OrderId, ct);
        return result.Status == OperationStatus.Ok
            ? Ok(result.Value)
            : BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = result.Error });
    }
}
