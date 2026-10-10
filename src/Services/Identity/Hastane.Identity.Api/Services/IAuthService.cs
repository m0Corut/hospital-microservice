using Hastane.Identity.Api.Dtos;
using Microsoft.AspNetCore.Identity;

namespace Hastane.Identity.Api.Services;

public enum RegisterStatus { Created, EmailTaken, Invalid }

public record RegisterOutcome(RegisterStatus Status, UserSummaryDto? User = null, IReadOnlyList<IdentityError>? Errors = null);

public enum LoginStatus { Success, InvalidCredentials, LockedOut }

public record LoginOutcome(LoginStatus Status, LoginResultDto? Result = null);

public enum ChangePasswordStatus { Success, UserNotFound, WrongCurrentPassword, LockedOut, SamePassword, Invalid }

public record ChangePasswordOutcome(ChangePasswordStatus Status, IReadOnlyList<IdentityError>? Errors = null);

public interface IAuthService
{
    Task<ChangePasswordOutcome> ChangePasswordAsync(string userId, ChangePasswordDto dto, CancellationToken ct);
    Task<RegisterOutcome> RegisterAsync(RegisterDto dto, CancellationToken ct);
    Task<LoginOutcome> LoginAsync(LoginDto dto, CancellationToken ct);
}
