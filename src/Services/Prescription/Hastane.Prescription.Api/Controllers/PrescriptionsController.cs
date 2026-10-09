using Hastane.Prescription.Api.Dtos;
using Hastane.Prescription.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Prescription.Api.Controllers;

[ApiController]
[Route("api/prescriptions")]
public class PrescriptionsController : ControllerBase
{
    private readonly IPrescriptionService _service;

    public PrescriptionsController(IPrescriptionService service) => _service = service;

    // Hepsi opsiyonel, birlikte kullanılabilir. En yeni reçete üstte.
    [HttpGet]
    public async Task<ActionResult<List<ResultPrescriptionDto>>> GetAll(
        [FromQuery] string? patientId, [FromQuery] string? doctorId, [FromQuery] Guid? appointmentId, CancellationToken ct)
        => Ok(await _service.GetAllAsync(patientId, doctorId, appointmentId, ct));

    // {id:guid} kısıtı: Guid biçiminde olmayan yol hiçbir rotayla eşleşmez, 500 yerine 404 döner.
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetByIdPrescriptionDto>> GetById(Guid id, CancellationToken ct)
    {
        var prescription = await _service.GetByIdAsync(id, ct);
        return prescription is null ? NotFound() : Ok(prescription);
    }

    [HttpPost]
    public async Task<ActionResult<GetByIdPrescriptionDto>> Create(CreatePrescriptionDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        if (result.Status == OperationStatus.Validation) return BadRequest(result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GetByIdPrescriptionDto>> Update(Guid id, UpdatePrescriptionDto dto, CancellationToken ct)
    {
        if (id != dto.Id) return BadRequest("Yoldaki id ile gövdedeki id uyuşmuyor.");
        var result = await _service.UpdateAsync(dto, ct);
        return result.Status switch
        {
            OperationStatus.Ok => Ok(result.Value),
            OperationStatus.Validation => BadRequest(result.Error),
            _ => NotFound(),
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
