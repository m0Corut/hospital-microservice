using System.Security.Claims;
using System.Text;
using Hastane.Shared.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hastane.TestSupport;

// Testlerde "giriş yapmış kullanıcı" üretir: Identity'ye hiç gitmeden, TestKeys ile imzalı RS256 token.
public static class TestJwt
{
    private static readonly JsonWebTokenHandler Handler = new();

    public static string Create(
        string userId,
        IEnumerable<string>? roles = null,
        string? doctorId = null,
        string? email = null,
        string? name = null,
        TimeSpan? expires = null,
        string issuer = JwtOptions.DefaultIssuer,
        string audience = JwtOptions.DefaultAudience)
        => Handler.CreateToken(Descriptor(userId, roles, doctorId, email, name, expires, issuer, audience,
            new SigningCredentials(TestKeys.SigningKey, SecurityAlgorithms.RsaSha256)));

    // Süresi çoktan dolmuş (ClockSkew'in de ötesinde) geçerli imzalı token.
    public static string CreateExpired(string userId, IEnumerable<string>? roles = null)
        => Create(userId, roles, expires: TimeSpan.FromMinutes(-10));

    // Geçerli token'ın imzası bozulmuş hali (son baytlar değiştirilmiş).
    public static string CreateWithBrokenSignature(string userId, IEnumerable<string>? roles = null)
    {
        var parts = Create(userId, roles).Split('.');
        var sig = parts[2].ToCharArray();
        sig[^3] = sig[^3] == 'A' ? 'B' : 'A';
        sig[^4] = sig[^4] == 'A' ? 'B' : 'A';
        return $"{parts[0]}.{parts[1]}.{new string(sig)}";
    }

    // Klasik saldırı 1: "alg: none", imza yok. Savunma: RequireSignedTokens + ValidAlgorithms.
    public static string CreateAlgNone(string userId, IEnumerable<string>? roles = null)
    {
        var parts = Create(userId, roles).Split('.');
        var header = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        return $"{header}.{parts[1]}.";
    }

    // Klasik saldırı 2 (alg confusion): AÇIK anahtarın metni HS256 "parolası" yapılıp aynı claim'lerle imzalanır.
    // Açık anahtar herkesin elinde olduğu için saldırgan bunu yapabilir; savunma ValidAlgorithms = yalnız RS256.
    public static string CreateHs256WithPublicKeyAsSecret(string userId, IEnumerable<string>? roles = null,
        string? publicKeyPem = null)
    {
        var secret = Encoding.UTF8.GetBytes(publicKeyPem ?? TestKeys.PublicKeyPem);
        var key = new SymmetricSecurityKey(secret);
        return Handler.CreateToken(Descriptor(userId, roles, null, null, null, null,
            JwtOptions.DefaultIssuer, JwtOptions.DefaultAudience,
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }

    private static SecurityTokenDescriptor Descriptor(string userId, IEnumerable<string>? roles, string? doctorId,
        string? email, string? name, TimeSpan? expires, string issuer, string audience, SigningCredentials credentials)
    {
        var now = DateTime.UtcNow;
        var exp = now + (expires ?? TimeSpan.FromMinutes(30));
        // Süresi geçmiş token için nbf, exp'ten önce olmalı (yoksa üretim aşamasında hata verir).
        var nbf = exp <= now ? exp.AddMinutes(-30) : now;

        var claims = new List<Claim>
        {
            new(HastaneClaims.Subject, userId),
            new(HastaneClaims.Email, email ?? $"{userId}@test.local"),
            new(HastaneClaims.Name, name ?? "Test Kullanıcı"),
            new(HastaneClaims.TokenId, Guid.NewGuid().ToString("N")),
        };
        foreach (var role in roles ?? [])
            claims.Add(new Claim(HastaneClaims.Role, role));
        if (!string.IsNullOrEmpty(doctorId))
            claims.Add(new Claim(HastaneClaims.DoctorId, doctorId));

        return new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = issuer,
            Audience = audience,
            IssuedAt = nbf,
            NotBefore = nbf,
            Expires = exp,
            SigningCredentials = credentials,
        };
    }
}
