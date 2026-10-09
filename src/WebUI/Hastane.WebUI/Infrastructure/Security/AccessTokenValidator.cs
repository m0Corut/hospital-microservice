using System.Security.Claims;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Hastane.WebUI.Infrastructure.Security;

public sealed record ValidatedToken(ClaimsPrincipal Principal, DateTimeOffset ExpiresAt);

public interface IAccessTokenValidator
{
    Task<ValidatedToken?> ValidateAsync(string token);
}

// Identity'nin verdiği JWT'yi API servisleriyle AYNI kurallarla doğrular (RS256, açık anahtar, issuer, audience, ömür).
// Parametreler Hastane.Shared'ın JwtBearer kaydından okunur: tek doğrulama tanımı, testte UseTestJwt() ile ezilebilir.
// Neden WebUI de doğrular: Identity'ye güvenmek yerine "bu token gerçekten bizim anahtarımızla mı imzalı" diye bakar;
// araya giren/yanlış yapılandırılmış bir yanıt oturum açtıramasın.
public sealed class AccessTokenValidator(IOptionsMonitor<JwtBearerOptions> bearerOptions, ILogger<AccessTokenValidator> logger)
    : IAccessTokenValidator
{
    private static readonly JsonWebTokenHandler Handler = new();

    public async Task<ValidatedToken?> ValidateAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var parameters = bearerOptions.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        var result = await Handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            logger.LogWarning("Giriş yanıtındaki token doğrulanamadı: {Reason}", result.Exception?.GetType().Name);
            return null;
        }

        var identity = new ClaimsIdentity(AuthConstants.CookieScheme, HastaneClaims.Name, HastaneClaims.Role);
        string[] kept = [HastaneClaims.Subject, HastaneClaims.Email, HastaneClaims.Name, HastaneClaims.Role, HastaneClaims.DoctorId];
        foreach (var claim in result.ClaimsIdentity.Claims.Where(c => kept.Contains(c.Type)))
            identity.AddClaim(new Claim(claim.Type, claim.Value));

        if (identity.FindFirst(HastaneClaims.Subject) is null) return null;

        return new ValidatedToken(new ClaimsPrincipal(identity), new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero));
    }
}
