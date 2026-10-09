using Hastane.Prescription.Api.Dtos;
using Hastane.Prescription.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Prescription.Api.Controllers;

[ApiController]
[Route("api/prescriptions")]
public class PrescriptionsController : ControllerBase
{
    // Okuma uçlarına giren roller; "kimin reçetesi" sorusunu rol değil servis (sahiplik) cevaplar.
    private const string AnyPrescriptionRole = $"{HastaneRoles.Admin},{HastaneRoles.Doctor},{HastaneRoles.Patient}";

    private readonly IPrescriptionService _service;

    public PrescriptionsController(IPrescriptionService service) => _service = service;

    // Hepsi opsiyonel, birlikte kullanılabilir. En yeni reçete üstte.
    [HttpGet]
    [Authorize(Roles = AnyPrescriptionRole)]
    public async Task<ActionResult<List<ResultPrescriptionDto>>> GetAll(
        [FromQuery] string? patientId, [FromQuery] string? doctorId, [FromQuery] Guid? appointmentId, CancellationToken ct)
        => Ok(await _service.GetAllAsync(patientId, doctorId, appointmentId, ct));

    // {id:guid} kısıtı: Guid biçiminde olmayan yol hiçbir rotayla eşleşmez, 500 yerine 404 döner.
    [HttpGet("{id:guid}")]
    [Authorize(Roles = AnyPrescriptionRole)]
    public async Task<ActionResult<GetByIdPrescriptionDto>> GetById(Guid id, CancellationToken ct)
    {
        var prescription = await _service.GetByIdAsync(id, ct);
        return prescription is null ? NotFound() : Ok(prescription);
    }

    [HttpPost]
    [Authorize(Policy = HastanePolicies.DoctorOrAdmin)]
    public async Task<ActionResult<GetByIdPrescriptionDto>> Create(CreatePrescriptionDto dto, CancellationToken ct)
    {
        // Admin başkası adına yazıyorsa hangi doktor olduğunu söylemek zorunda; doktor için DoctorId aranmaz (token'dan).
        if (User.IsInRole(HastaneRoles.Admin) && string.IsNullOrWhiteSpace(dto.DoctorId))
        {
            ModelState.AddModelError(nameof(dto.DoctorId), "Admin için DoctorId zorunludur.");
            return ValidationProblem(ModelState);
        }

        var result = await _service.CreateAsync(dto, ct);
        if (result.Status == OperationStatus.Forbidden) return Forbid();
        if (result.Status == OperationStatus.Validation) return BadRequest(result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = HastanePolicies.DoctorOrAdmin)]
    public async Task<ActionResult<GetByIdPrescriptionDto>> Update(Guid id, UpdatePrescriptionDto dto, CancellationToken ct)
    {
        if (id != dto.Id) return BadRequest("Yoldaki id ile gövdedeki id uyuşmuyor.");
        var result = await _service.UpdateAsync(dto, ct);
        return result.Status switch
        {
            OperationStatus.Ok => Ok(result.Value),
            OperationStatus.Validation => BadRequest(result.Error),
            OperationStatus.Forbidden => Forbid(),
            _ => NotFound(),
        };
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = HastanePolicies.DoctorOrAdmin)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) switch
        {
            OperationStatus.Ok => NoContent(),
            OperationStatus.Forbidden => Forbid(),
            _ => NotFound(),
        };
}
