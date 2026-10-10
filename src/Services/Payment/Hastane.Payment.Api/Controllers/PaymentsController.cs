using Hastane.Payment.Api.Dtos;
using Hastane.Payment.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Payment.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    // Her eylem kendi yetkisini AÇIKÇA taşır (sınıf düzeyi [Authorize] ile metot düzeyi birleşip karışmasın).
    private const string AnyPaymentRole = $"{HastaneRoles.Admin},{HastaneRoles.Doctor},{HastaneRoles.Patient}";
    private const string PatientOrAdmin = $"{HastaneRoles.Patient},{HastaneRoles.Admin}";

    private readonly IPaymentService _service;

    public PaymentsController(IPaymentService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Roles = AnyPaymentRole)]
    public async Task<ActionResult<PagedResult<PaymentResultDto>>> GetAll([FromQuery] PaymentListQuery query, CancellationToken ct)
        => ToActionResult(await _service.GetAllAsync(query, ct));

    // {id:guid} kısıtı: Guid biçiminde olmayan yol hiçbir rotayla eşleşmez, 500 yerine 404 döner.
    [HttpGet("{id:guid}")]
    [Authorize(Roles = AnyPaymentRole)]
    public async Task<ActionResult<PaymentResultDto>> GetById(Guid id, CancellationToken ct)
    {
        var payment = await _service.GetByIdAsync(id, ct);
        return payment is null ? NotFound() : Ok(payment);
    }

    // Idempotency-Key zorunlu: istemci her "ödeme niyeti" için bir anahtar üretir, yeniden denemede AYNISINI yollar.
    [HttpPost]
    [Authorize(Roles = PatientOrAdmin)]
    public async Task<ActionResult<PaymentResultDto>> Create(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CreatePaymentDto dto, CancellationToken ct)
    {
        if (!IdempotencyKeyRules.IsValid(idempotencyKey))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = IdempotencyKeyRules.Message });

        var result = await _service.CreateAsync(idempotencyKey, dto, ct);
        return result.Status == OperationStatus.Created
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : ToActionResult(result);
    }

    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<ActionResult<PaymentResultDto>> Refund(Guid id, CancellationToken ct)
        => ToActionResult(await _service.RefundAsync(id, ct));

    private ActionResult<T> ToActionResult<T>(OperationResult<T> result) => result.Status switch
    {
        OperationStatus.Ok or OperationStatus.Created => Ok(result.Value),
        OperationStatus.NotFound => result.Error is null
            ? NotFound()
            : NotFound(new ProblemDetails { Status = 404, Title = "Bulunamadı", Detail = result.Error }),
        OperationStatus.Forbidden => Forbid(),
        OperationStatus.Validation => BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = result.Error }),
        OperationStatus.Conflict => Conflict(new ProblemDetails { Status = 409, Title = "Çakışma", Detail = result.Error }),
        // 503: randevu/doktor doğrulaması yapılamadı. Kart çekilmedi, kayıt oluşmadı; aynı anahtarla tekrar denenebilir.
        OperationStatus.ServiceUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable,
            new ProblemDetails { Status = 503, Title = "Servis kullanılamıyor", Detail = result.Error }),
        OperationStatus.BadGateway => StatusCode(StatusCodes.Status502BadGateway,
            new ProblemDetails { Status = 502, Title = "Ödeme işlenemedi", Detail = result.Error }),
        // 402 Payment Required: istek geçerli, ama ödeme sağlayıcı tarafından reddedildi. Gövde yine ödeme sonucu (Failed).
        OperationStatus.PaymentRequired => StatusCode(StatusCodes.Status402PaymentRequired, result.Value),
        _ => StatusCode(500),
    };
}
