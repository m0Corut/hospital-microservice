using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Identity.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private const int MaxIds = 100;
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    [HttpGet("{id}")]
    public async Task<ActionResult<UserSummaryDto>> GetById(string id, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(id, ct);
        return user is null ? NotFound() : Ok(user);
    }

    // Diğer servisler (Patient, Appointment) birden çok kullanıcıyı tek istekte ister: ?ids=a,b,c
    [HttpGet]
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
}
