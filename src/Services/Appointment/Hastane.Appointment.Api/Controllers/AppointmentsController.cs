using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using Hastane.Appointment.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Appointment.Api.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    // Randevu uçlarına girebilen roller; "kimin randevusu" sorusunu rol değil servis (sahiplik) cevaplar.
    private const string AnyAppointmentRole = $"{HastaneRoles.Admin},{HastaneRoles.Doctor},{HastaneRoles.Patient}";
    private const string PatientOrAdmin = $"{HastaneRoles.Patient},{HastaneRoles.Admin}";

    private readonly IAppointmentService _service;
    private readonly IAppointmentDetailService _detailService;

    public AppointmentsController(IAppointmentService service, IAppointmentDetailService detailService)
    {
        _service = service;
        _detailService = detailService;
    }

    // Hepsi opsiyonel, birlikte kullanılabilir. from/to: AppointmentDate için uçlar dahil aralık.
    [HttpGet]
    [Authorize(Roles = AnyAppointmentRole)]
    public async Task<ActionResult<List<ResultAppointmentDto>>> GetAll(
        [FromQuery] string? doctorId, [FromQuery] string? patientId, [FromQuery] AppointmentStatus? status,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _service.GetAllAsync(doctorId, patientId, status, from, to, ct));

    // {id:guid} kısıtı: Guid biçiminde olmayan yol (".../abc") hiçbir rotayla eşleşmez, 500 yerine 404 döner.
    [HttpGet("{id:guid}")]
    [Authorize(Roles = AnyAppointmentRole)]
    public async Task<ActionResult<GetByIdAppointmentDto>> GetById(Guid id, CancellationToken ct)
    {
        var appointment = await _service.GetByIdAsync(id, ct);
        return appointment is null ? NotFound() : Ok(appointment);
    }

    [HttpPost]
    [Authorize(Roles = PatientOrAdmin)]
    public async Task<ActionResult<ResultAppointmentDto>> Create(CreateAppointmentDto dto, CancellationToken ct)
    {
        // Admin başkası adına açıyorsa hangi hasta olduğunu söylemek zorunda; hasta için PatientId aranmaz (token'dan).
        if (User.IsInRole(HastaneRoles.Admin) && string.IsNullOrWhiteSpace(dto.PatientId))
        {
            ModelState.AddModelError(nameof(dto.PatientId), "Admin için PatientId zorunludur.");
            return ValidationProblem(ModelState);
        }

        var result = await _service.CreateAsync(dto, ct);
        return result.Status == OperationStatus.Created
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : ToActionResult(result);
    }

    // Yeniden planlama: yalnız şube ve tarih.
    [HttpPut("{id:guid}")]
    [Authorize(Roles = AnyAppointmentRole)]
    public async Task<ActionResult<ResultAppointmentDto>> Reschedule(Guid id, UpdateAppointmentDto dto, CancellationToken ct)
    {
        if (id != dto.Id) return BadRequest("Yoldaki id ile gövdedeki id uyuşmuyor.");
        return ToActionResult(await _service.RescheduleAsync(dto, ct));
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Roles = AnyAppointmentRole)]
    public async Task<ActionResult<ResultAppointmentDto>> UpdateStatus(Guid id, UpdateStatusDto dto, CancellationToken ct)
        => ToActionResult(await _service.UpdateStatusAsync(id, dto.Status!.Value, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpGet("{id:guid}/detail")]
    [Authorize(Roles = AnyAppointmentRole)]
    public async Task<ActionResult<AppointmentDetailDto>> GetDetail(Guid id, CancellationToken ct)
    {
        var detail = await _detailService.GetByAppointmentIdAsync(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    // Upsert: detay yoksa oluşturur (201), varsa günceller (200). Randevu yoksa 404.
    [HttpPut("{id:guid}/detail")]
    [Authorize(Policy = HastanePolicies.DoctorOrAdmin)]
    public async Task<ActionResult<AppointmentDetailDto>> UpsertDetail(Guid id, UpsertAppointmentDetailDto dto, CancellationToken ct)
    {
        var result = await _detailService.UpsertAsync(id, dto, ct);
        return result.Status == OperationStatus.Created
            ? CreatedAtAction(nameof(GetDetail), new { id }, result.Value)
            : ToActionResult(result);
    }

    private ActionResult<T> ToActionResult<T>(OperationResult<T> result) => result.Status switch
    {
        OperationStatus.Ok or OperationStatus.Created => Ok(result.Value),
        OperationStatus.NotFound => NotFound(),
        OperationStatus.Forbidden => Forbid(),
        _ => Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Çakışma", Detail = result.Error }),
    };
}
