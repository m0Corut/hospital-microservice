using System.Security.Claims;
using Hastane.Identity.Api.Dtos;
using Hastane.Shared.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hastane.Identity.Api.Services;

// Token'ı ÜRETEN tek yer. Özel anahtar yalnız burada (Identity) bilinir; diğer servisler yalnız doğrular.
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _clock;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
    {
        _options = options.Value; // yapılandırma doğrulaması burada çalışır (özel anahtar yoksa açılışta hata)
        _clock = clock;
        var key = JwtKeys.CreatePrivateKey(_options.PrivateKeyPem!);
        _credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
    }

    public AccessToken CreateAccessToken(UserSummaryDto user)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(HastaneClaims.Subject, user.Id),
            new(HastaneClaims.Email, user.Email),
            new(HastaneClaims.Name, $"{user.FirstName} {user.LastName}".Trim()),
            new(HastaneClaims.TokenId, Guid.NewGuid().ToString("N")),
        };
        // Her rol ayrı bir "role" claim'i: [Authorize(Roles=...)] ve IsInRole bunu okur.
        claims.AddRange(user.Roles.Select(r => new Claim(HastaneClaims.Role, r)));
        // doctor_id yalnız Doctor rolündeki ve bağlanmış kullanıcıda.
        if (!string.IsNullOrEmpty(user.DoctorId) && user.Roles.Contains(HastaneRoles.Doctor))
            claims.Add(new Claim(HastaneClaims.DoctorId, user.DoctorId));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = _credentials, // kid başlığı anahtarın KeyId'sinden gelir
        };
        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}
