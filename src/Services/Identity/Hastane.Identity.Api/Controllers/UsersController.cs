using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Identity.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = HastanePolicies.AuthenticatedUser)]
public class UsersController : ControllerBase
{
    private const int MaxIds = 100;
    private readonly IUserService _users;
    private readonly IRoleService _roles;
    private readonly ICurrentUser _currentUser;

    public UsersController(IUserService users, IRoleService roles, ICurrentUser currentUser)
    {
        _users = users;
        _roles = roles;
        _currentUser = currentUser;
    }

    // Kural: kendisi, Doctor ya da Admin. Başkasının kaydına bakan Patient'a 404 (kaydın var olduğu sızmasın).
    [HttpGet("{id}")]
    public async Task<ActionResult<UserSummaryDto>> GetById(string id, CancellationToken ct)
    {
        var isSelf = Guid.TryParse(id, out var requested) && Guid.TryParse(_currentUser.UserId, out var me)
            && requested == me;
        if (!isSelf && !_currentUser.IsInRole(HastaneRoles.Doctor) && !_currentUser.IsAdmin)
            return NotFound();

        var user = await _users.GetByIdAsync(id, ct);
        return user is null ? NotFound() : Ok(user);
    }

    // Diğer servisler (Patient, Appointment) birden çok kullanıcıyı tek istekte ister: ?ids=a,b,c
    [HttpGet]
    [Authorize(Policy = HastanePolicies.DoctorOrAdmin)]
    public async Task<ActionResult<List<UserSummaryDto>>> GetByIds([FromQuery] string? ids, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ids)) return Ok(new List<UserSummaryDto>());

        var parsed = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g.ToString() : null)
            .OfType<string>()
            .Distinct()
            .ToList();
        if (parsed.Count > MaxIds)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Çok fazla id",
                detail: $"Tek istekte en çok {MaxIds} kullanıcı istenebilir.");

        return Ok(await _users.GetByIdsAsync(parsed, ct));
    }

    [HttpPost("{id}/roles")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<ActionResult<UserSummaryDto>> AddRole(string id, AssignRoleDto dto, CancellationToken ct)
        => ToResult(await _roles.AddToRoleAsync(id, dto.Role, ct));

    [HttpDelete("{id}/roles/{role}")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<ActionResult<UserSummaryDto>> RemoveRole(string id, string role, CancellationToken ct)
        => ToResult(await _roles.RemoveFromRoleAsync(id, role, ct));

    [HttpPut("{id}/doctor-link")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<ActionResult<UserSummaryDto>> LinkDoctor(string id, LinkDoctorDto dto, CancellationToken ct)
        => ToResult(await _roles.LinkDoctorAsync(id, dto.DoctorId, ct));

    private ActionResult<UserSummaryDto> ToResult(UserOpOutcome outcome) => outcome.Status switch
    {
        UserOpStatus.Ok => Ok(outcome.User),
        UserOpStatus.UserNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Bulunamadı",
            detail: "Kullanıcı bulunamadı."),
        UserOpStatus.RoleNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Bulunamadı",
            detail: "Rol bulunamadı."),
        UserOpStatus.AlreadyInRole => Conflict("Kullanıcı zaten bu rolde."),
        UserOpStatus.NotInRole => Problem(statusCode: StatusCodes.Status404NotFound, title: "Bulunamadı",
            detail: "Kullanıcı bu rolde değil."),
        UserOpStatus.LastAdmin => Conflict("Sistemin son Admin'inin Admin rolü kaldırılamaz."),
        UserOpStatus.NotADoctor => Conflict("Doktor bağı için kullanıcının Doctor rolü olmalı."),
        UserOpStatus.DoctorAlreadyLinked => Conflict("Bu doktor kaydı başka bir kullanıcıya bağlı."),
        _ => throw new InvalidOperationException($"Beklenmeyen durum: {outcome.Status}"),
    };

    private ObjectResult Conflict(string detail)
        => Problem(statusCode: StatusCodes.Status409Conflict, title: "Çakışma", detail: detail);
}
