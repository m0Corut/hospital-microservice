using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Doctor.Api.Controllers;

[ApiController]
[Route("api/doctors")]
public class DoctorsController : ControllerBase
{
    private readonly IDoctorService _service;

    public DoctorsController(IDoctorService service) => _service = service;

    // GET api/doctors?branchId=... ile branşa göre süzülür.
    [HttpGet]
    public async Task<ActionResult<List<ResultDoctorDto>>> GetAll([FromQuery] string? branchId, CancellationToken ct)
        => Ok(string.IsNullOrWhiteSpace(branchId)
            ? await _service.GetAllAsync(ct)
            : await _service.GetByBranchIdAsync(branchId, ct));

    [HttpGet("{id}")]
    public async Task<ActionResult<GetByIdDoctorDto>> GetById(string id, CancellationToken ct)
    {
        var doctor = await _service.GetByIdAsync(id, ct);
        return doctor is null ? NotFound() : Ok(doctor);
    }

    [HttpPost]
    public async Task<ActionResult<ResultDoctorDto>> Create(CreateDoctorDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateDoctorDto dto, CancellationToken ct)
    {
        if (id != dto.Id) return BadRequest("Yoldaki id ile gövdedeki id uyuşmuyor.");
        return await _service.UpdateAsync(dto, ct) ? NoContent() : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
