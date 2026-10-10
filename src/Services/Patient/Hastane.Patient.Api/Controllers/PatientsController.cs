using Hastane.Patient.Api.Dtos;
using Hastane.Patient.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Patient.Api.Controllers;

[ApiController]
[Route("api/patients")]
public class PatientsController : ControllerBase
{
    // Her eylem kendi yetkisini AÇIKÇA taşır (sınıf düzeyi [Authorize] ile metot düzeyi birleşip karışmasın).
    private const string AnyRole = $"{HastaneRoles.Admin},{HastaneRoles.Doctor},{HastaneRoles.Patient}";

    private readonly IPatientService _service;

    public PatientsController(IPatientService service)
    {
        _service = service;
    }

    // "me" sabit parça olduğundan {userId} parametreli rotadan önce eşleşir.
    [HttpGet("me")]
    [Authorize(Policy = HastanePolicies.PatientOnly)]
    public async Task<ActionResult<PatientProfileDto>> GetMine(CancellationToken ct)
        => Ok(await _service.GetMineAsync(ct));

    [HttpPut("me")]
    [Authorize(Policy = HastanePolicies.PatientOnly)]
    public async Task<ActionResult<PatientProfileDto>> UpsertMine(UpdatePatientProfileDto dto, CancellationToken ct)
        => Ok(await _service.UpsertMineAsync(dto, ct));

    [HttpGet("{userId}")]
    [Authorize(Roles = AnyRole)]
    public async Task<ActionResult<PatientProfileDto>> GetByUserId(string userId, CancellationToken ct)
        => ToActionResult(await _service.GetByUserIdAsync(userId, ct));

    [HttpGet("me/favorites")]
    [Authorize(Policy = HastanePolicies.PatientOnly)]
    public async Task<ActionResult<List<FavoriteDoctorDto>>> GetFavorites(CancellationToken ct)
        => Ok(await _service.GetFavoritesAsync(ct));

    [HttpPut("me/favorites/{doctorId}")]
    [Authorize(Policy = HastanePolicies.PatientOnly)]
    public async Task<IActionResult> AddFavorite(string doctorId, CancellationToken ct)
        => ToActionResult(await _service.AddFavoriteAsync(doctorId, ct)).Result!;

    [HttpDelete("me/favorites/{doctorId}")]
    [Authorize(Policy = HastanePolicies.PatientOnly)]
    public async Task<IActionResult> RemoveFavorite(string doctorId, CancellationToken ct)
    {
        await _service.RemoveFavoriteAsync(doctorId, ct);
        return NoContent();
    }

    private ActionResult<T> ToActionResult<T>(OperationResult<T> result) => result.Status switch
    {
        OperationStatus.Ok => Ok(result.Value),
        OperationStatus.NoContent => NoContent(),
        OperationStatus.NotFound => result.Error is null
            ? NotFound()
            : NotFound(new ProblemDetails { Status = 404, Title = "Bulunamadı", Detail = result.Error }),
        OperationStatus.Validation => BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = result.Error }),
        OperationStatus.Conflict => Conflict(new ProblemDetails { Status = 409, Title = "Çakışma", Detail = result.Error }),
        OperationStatus.ServiceUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable,
            new ProblemDetails { Status = 503, Title = "Servis kullanılamıyor", Detail = result.Error }),
        _ => StatusCode(500),
    };
}
