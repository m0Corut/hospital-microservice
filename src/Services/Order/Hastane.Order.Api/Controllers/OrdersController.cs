using Hastane.Order.Api.Dtos;
using Hastane.Order.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Order.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    // Her eylem kendi yetkisini AÇIKÇA taşır (sınıf düzeyi [Authorize] ile metot düzeyi birleşip karışmasın).
    private const string PatientOrAdmin = $"{HastaneRoles.Patient},{HastaneRoles.Admin}";

    private readonly IOrderService _service;

    public OrdersController(IOrderService service)
    {
        _service = service;
    }

    // Idempotency-Key opsiyonel: verilirse aynı anahtar aynı siparişi döndürür (çift tıklama / yeniden deneme).
    // Fiyat/hasta gövdeden alınmaz: hasta token'dan, fiyat Market'ten.
    [HttpPost("checkout")]
    [Authorize(Roles = HastaneRoles.Patient)]
    public async Task<ActionResult<GetByIdOrderDto>> Checkout(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CheckoutDto dto, CancellationToken ct)
    {
        if (idempotencyKey is not null && !IdempotencyKeyRules.IsValid(idempotencyKey))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = IdempotencyKeyRules.Message });

        var name = User.FindFirst(HastaneClaims.Name)?.Value ?? User.FindFirst(HastaneClaims.Email)?.Value ?? string.Empty;
        var result = await _service.CheckoutAsync(idempotencyKey, dto, name, ct);
        return result.Status == OperationStatus.Created
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : ToActionResult(result);
    }

    [HttpGet]
    [Authorize(Roles = PatientOrAdmin)]
    public async Task<ActionResult<PagedResult<ResultOrderDto>>> GetAll([FromQuery] OrderListQuery query, CancellationToken ct)
        => Ok(await _service.GetAllAsync(query, ct));

    // {id:guid} kısıtı: Guid biçiminde olmayan yol hiçbir rotayla eşleşmez, 500 yerine 404 döner.
    [HttpGet("{id:guid}")]
    [Authorize(Roles = PatientOrAdmin)]
    public async Task<ActionResult<GetByIdOrderDto>> GetById(Guid id, CancellationToken ct)
    {
        var order = await _service.GetByIdAsync(id, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<ActionResult<GetByIdOrderDto>> UpdateStatus(Guid id, UpdateOrderStatusDto dto, CancellationToken ct)
        => ToActionResult(await _service.UpdateStatusAsync(id, dto.Status!.Value, ct));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = PatientOrAdmin)]
    public async Task<ActionResult<GetByIdOrderDto>> Cancel(Guid id, CancelOrderDto? dto, CancellationToken ct)
        => ToActionResult(await _service.CancelAsync(id, dto?.Reason, ct));

    private ActionResult<T> ToActionResult<T>(OperationResult<T> result) => result.Status switch
    {
        OperationStatus.Ok or OperationStatus.Created => Ok(result.Value),
        OperationStatus.NotFound => NotFound(),
        OperationStatus.Validation => BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = result.Error }),
        OperationStatus.Conflict => Conflict(new ProblemDetails { Status = 409, Title = "Çakışma", Detail = result.Error }),
        // 503: Market'e ulaşılamadı. Sepet/ürün okunamadıysa sipariş hiç yazılmadı; stok ayrılamadıysa Cancelled yazıldı.
        OperationStatus.ServiceUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable,
            new ProblemDetails { Status = 503, Title = "Servis kullanılamıyor", Detail = result.Error }),
        _ => StatusCode(500),
    };
}
