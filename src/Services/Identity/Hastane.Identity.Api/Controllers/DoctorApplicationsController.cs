using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Entities;
using Hastane.Identity.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Identity.Api.Controllers;

// Doktor başvurularını yönetmek yalnız Admin'in işi. Onaydan sonra doktor KAYDI oluşturma ve doctor-link mevcut
// Admin akışıyla (PUT users/{id}/doctor-link) ayrıca yapılır.
[ApiController]
[Route("api/doctor-applications")]
[Authorize(Policy = HastanePolicies.AdminOnly)]
public class DoctorApplicationsController : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxPage = 10000;
    private readonly IDoctorApplicationService _applications;
    private readonly ICurrentUser _currentUser;

    public DoctorApplicationsController(IDoctorApplicationService applications, ICurrentUser currentUser)
    {
        _applications = applications;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize, CancellationToken ct = default)
    {
        DoctorApplicationStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var text = status.Trim();
            // Sayı değil ad kabul edilir ("1" ya da "99" sessizce geçmesin).
            if (!int.TryParse(text, out _) && Enum.TryParse<DoctorApplicationStatus>(text, ignoreCase: true, out var value)
                && Enum.IsDefined(value))
                parsed = value;
            else
                ModelState.AddModelError(nameof(status), "status Pending, Approved ya da Rejected olmalı.");
        }
        if (page < 1 || page > MaxPage)
            ModelState.AddModelError(nameof(page), $"page 1 ile {MaxPage} arasında olmalı.");
        if (pageSize < 1 || pageSize > MaxPageSize)
            ModelState.AddModelError(nameof(pageSize), $"pageSize 1 ile {MaxPageSize} arasında olmalı.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        return Ok(await _applications.ListAsync(parsed, page, pageSize, ct));
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<DoctorApplicationDto>> Approve(Guid id, CancellationToken ct)
        => ToResult(await _applications.ApproveAsync(id, _currentUser.UserId ?? string.Empty, ct));

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<DoctorApplicationDto>> Reject(Guid id, RejectDoctorApplicationDto dto, CancellationToken ct)
        => ToResult(await _applications.RejectAsync(id, _currentUser.UserId ?? string.Empty, dto.Note, ct));

    private ActionResult<DoctorApplicationDto> ToResult(DecisionOutcome outcome) => outcome.Status switch
    {
        DecisionStatus.Decided => Ok(outcome.Application),
        DecisionStatus.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Bulunamadı",
            detail: "Başvuru bulunamadı."),
        _ => Problem(statusCode: StatusCodes.Status409Conflict, title: "Çakışma",
            detail: "Bu başvuru için zaten karar verilmiş."),
    };
}
