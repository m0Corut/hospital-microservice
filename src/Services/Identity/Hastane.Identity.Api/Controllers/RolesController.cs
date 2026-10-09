using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Identity.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Authorize(Policy = HastanePolicies.AdminOnly)]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roles;

    public RolesController(IRoleService roles) => _roles = roles;

    [HttpGet]
    public async Task<ActionResult<List<RoleDto>>> List(CancellationToken ct)
        => Ok(await _roles.ListRolesAsync(ct));

    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create(CreateRoleDto dto, CancellationToken ct)
    {
        var status = await _roles.CreateRoleAsync(dto.Name, ct);
        return status switch
        {
            RoleCreateStatus.Created => StatusCode(StatusCodes.Status201Created, new RoleDto { Name = dto.Name.Trim() }),
            RoleCreateStatus.AlreadyExists => Problem(statusCode: StatusCodes.Status409Conflict, title: "Çakışma",
                detail: "Bu rol zaten var."),
            _ => Problem(statusCode: StatusCodes.Status400BadRequest, title: "Geçersiz rol",
                detail: "Rol oluşturulamadı."),
        };
    }
}
