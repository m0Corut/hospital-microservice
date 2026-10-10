using Hastane.Queue.Api.Dtos;
using Hastane.Queue.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Queue.Api.Controllers;

[ApiController]
[Route("api/queue")]
public class QueueController(IQueueService service) : ControllerBase
{
    // Sıraya giriş. Yeni kayıt 201, aynı randevu ikinci kez 200 (mevcut kayıt; idempotent).
    // Ad token'daki "name" claim'inden gelir ve maskelenir ("Can Y."); istemci ad gönderemez.
    [HttpPost("check-in")]
    [Authorize(Policy = HastanePolicies.PatientOnly)]
    public async Task<ActionResult<MyQueueEntryDto>> CheckIn(CheckInDto dto, CancellationToken ct)
    {
        var name = User.FindFirst(HastaneClaims.Name)?.Value;
        var result = await service.CheckInAsync(dto.AppointmentId, name, ct);
        return result.Status == OperationStatus.Created
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ToActionResult(result);
    }

    [HttpGet("me/today")]
    [Authorize(Policy = HastanePolicies.PatientOnly)]
    public async Task<ActionResult<List<MyQueueEntryDto>>> MineToday(CancellationToken ct)
        => Ok(await service.GetMineTodayAsync(ct));

    // Doktor: kendi sırası (doctorId yok sayılır). Admin: ?doctorId= zorunlu.
    [HttpGet("doctor/today")]
    [Authorize(Policy = HastanePolicies.DoctorOrAdmin)]
    public async Task<ActionResult<List<QueueEntryDto>>> DoctorToday([FromQuery] string? doctorId, CancellationToken ct)
        => ToActionResult(await service.GetDoctorTodayAsync(doctorId, ct));

    [HttpPost("doctor/call-next")]
    [Authorize(Policy = HastanePolicies.DoctorOnly)]
    public async Task<ActionResult<QueueEntryDto>> CallNext(CancellationToken ct)
        => ToActionResult(await service.CallNextAsync(ct));

    [HttpPost("{id:guid}/call")]
    [Authorize(Policy = HastanePolicies.DoctorOrAdmin)]
    public async Task<ActionResult<QueueEntryDto>> Call(Guid id, CancellationToken ct)
        => ToActionResult(await service.CallAsync(id, ct));

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = HastanePolicies.DoctorOrAdmin)]
    public async Task<ActionResult<QueueEntryDto>> Complete(Guid id, CancellationToken ct)
        => ToActionResult(await service.CompleteAsync(id, ct));

    [HttpPost("{id:guid}/no-show")]
    [Authorize(Policy = HastanePolicies.DoctorOrAdmin)]
    public async Task<ActionResult<QueueEntryDto>> NoShow(Guid id, CancellationToken ct)
        => ToActionResult(await service.NoShowAsync(id, ct));

    // Bekleme salonu ekranı: ANONİM ve kimliksiz (yalnız numara, maskeli ad, durum). Hasta/randevu kimliği yok.
    [HttpGet("display")]
    [AllowAnonymous]
    public async Task<ActionResult<QueueDisplayDto>> Display([FromQuery] string? doctorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doctorId) || doctorId.Length > 64)
        {
            ModelState.AddModelError(nameof(doctorId), "doctorId zorunludur.");
            return ValidationProblem(ModelState);
        }

        return Ok(await service.GetDisplayAsync(doctorId, ct));
    }

    private ActionResult<T> ToActionResult<T>(OperationResult<T> result) => result.Status switch
    {
        OperationStatus.Ok or OperationStatus.Created => Ok(result.Value),
        OperationStatus.NotFound => NotFound(),
        OperationStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
            new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = "Yetkiniz yok", Detail = result.Error }),
        OperationStatus.Validation => BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Geçersiz istek", Detail = result.Error }),
        OperationStatus.ServiceUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable,
            new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable, Title = "Servis kullanılamıyor", Detail = result.Error }),
        _ => Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Çakışma", Detail = result.Error }),
    };
}
