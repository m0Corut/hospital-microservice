using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Services;
using Hastane.Doctor.Api.Validation;
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

    // Sabit rotalar ("search", "batch") ASP.NET Core yönlendirmesinde {id} gibi parametreli rotadan ÖNCE kazanır;
    // yine de metodlar {id}'den önce tanımlı ve testle kilitli.
    // GET api/doctors/search?text=&gender=&branchIds=a,b&city=&minFee=&maxFee=&date=2026-10-12&sort=name&page=1&pageSize=10
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<DoctorSearchItemDto>>> Search([FromQuery] DoctorSearchQuery query, CancellationToken ct)
    {
        try { return Ok(await _service.SearchAsync(query, ct)); }
        catch (DoctorValidationException ex) { return ValidationProblem(new ValidationProblemDetails(ex.Errors)); }
    }

    // GET api/doctors/batch?ids=a,b,c
    [HttpGet("batch")]
    public async Task<ActionResult<List<DoctorSummaryDto>>> Batch([FromQuery] string? ids, CancellationToken ct)
    {
        try { return Ok(await _service.GetBatchAsync(ids, ct)); }
        catch (DoctorValidationException ex) { return ValidationProblem(new ValidationProblemDetails(ex.Errors)); }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GetByIdDoctorDto>> GetById(string id, CancellationToken ct)
    {
        var doctor = await _service.GetByIdAsync(id, ct);
        return doctor is null ? NotFound() : Ok(doctor);
    }

    [HttpPost]
    public async Task<ActionResult<ResultDoctorDto>> Create(CreateDoctorDto dto, CancellationToken ct)
    {
        try
        {
            var created = await _service.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (DoctorValidationException ex) { return ValidationProblem(new ValidationProblemDetails(ex.Errors)); }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateDoctorDto dto, CancellationToken ct)
    {
        if (id != dto.Id) return BadRequest("Yoldaki id ile gövdedeki id uyuşmuyor.");
        try { return await _service.UpdateAsync(dto, ct) ? NoContent() : NotFound(); }
        catch (DoctorValidationException ex) { return ValidationProblem(new ValidationProblemDetails(ex.Errors)); }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
