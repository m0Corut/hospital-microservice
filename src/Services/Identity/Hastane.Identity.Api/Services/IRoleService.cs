using Hastane.Identity.Api.Dtos;

namespace Hastane.Identity.Api.Services;

public enum RoleCreateStatus { Created, AlreadyExists, Invalid }

public enum UserOpStatus
{
    Ok,
    UserNotFound,
    RoleNotFound,
    AlreadyInRole,
    NotInRole,
    LastAdmin,
    NotADoctor,
    DoctorAlreadyLinked,
}

public record UserOpOutcome(UserOpStatus Status, UserSummaryDto? User = null);

public interface IRoleService
{
    Task<List<RoleDto>> ListRolesAsync(CancellationToken ct);
    Task<RoleCreateStatus> CreateRoleAsync(string name, CancellationToken ct);
    Task<UserOpOutcome> AddToRoleAsync(string userId, string role, CancellationToken ct);
    Task<UserOpOutcome> RemoveFromRoleAsync(string userId, string role, CancellationToken ct);
    Task<UserOpOutcome> LinkDoctorAsync(string userId, string doctorId, CancellationToken ct);
}
