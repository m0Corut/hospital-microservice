using Hastane.Branch.Api.Dtos;
using Hastane.Branch.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Branch.Api.Controllers;

[ApiController]
[Route("api/branches")]
public class BranchesController : ControllerBase
{
    private readonly IBranchService _service;

    // Servis new ile burada üretilmez; DI konteyneri constructor'dan verir.
    public BranchesController(IBranchService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<List<ResultBranchDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("{id}")]
    public async Task<ActionResult<GetByIdBranchDto>> GetById(string id, CancellationToken ct)
    {
        var branch = await _service.GetByIdAsync(id, ct);
        return branch is null ? NotFound() : Ok(branch);
    }

    [HttpPost]
    public async Task<ActionResult<ResultBranchDto>> Create(CreateBranchDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateBranchDto dto, CancellationToken ct)
    {
        if (id != dto.Id) return BadRequest("Yoldaki id ile gövdedeki id uyuşmuyor.");
        return await _service.UpdateAsync(dto, ct) ? NoContent() : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
