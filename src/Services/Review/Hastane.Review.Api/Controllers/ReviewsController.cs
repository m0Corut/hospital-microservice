using Hastane.Review.Api.Dtos;
using Hastane.Review.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Review.Api.Controllers;

[ApiController]
[Route("api/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _service;

    public ReviewsController(IReviewService service) => _service = service;

    // GET api/reviews?doctorId=... ile doktora göre süzülür (yeniden eskiye).
    [HttpGet]
    public async Task<ActionResult<List<ResultReviewDto>>> GetAll([FromQuery] string? doctorId, CancellationToken ct)
        => Ok(string.IsNullOrWhiteSpace(doctorId)
            ? await _service.GetAllAsync(ct)
            : await _service.GetByDoctorIdAsync(doctorId, ct));

    [HttpGet("{id}")]
    public async Task<ActionResult<ResultReviewDto>> GetById(string id, CancellationToken ct)
    {
        var review = await _service.GetByIdAsync(id, ct);
        return review is null ? NotFound() : Ok(review);
    }

    // Doktorun puan özeti. Yorumu olmayan doktor için de 200 (ReviewCount=0).
    [HttpGet("doctors/{doctorId}/summary")]
    public async Task<ActionResult<DoctorRatingSummaryDto>> GetDoctorSummary(string doctorId, CancellationToken ct)
        => Ok(await _service.GetDoctorSummaryAsync(doctorId, ct));

    [HttpPost]
    public async Task<ActionResult<ResultReviewDto>> Create(CreateReviewDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateReviewDto dto, CancellationToken ct)
    {
        if (id != dto.Id) return BadRequest("Yoldaki id ile gövdedeki id uyuşmuyor.");
        return await _service.UpdateAsync(dto, ct) ? NoContent() : NotFound();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
