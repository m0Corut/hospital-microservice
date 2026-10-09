using Hastane.Identity.Api.Dtos;

namespace Hastane.Identity.Api.Services;

public record AccessToken(string Value, DateTime ExpiresAt);

public interface IJwtTokenService
{
    AccessToken CreateAccessToken(UserSummaryDto user);
}
