using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hastane.Shared.Security;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddHastaneJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // PostConfigure: tüm Configure'lardan sonra, doğrulayıcılardan (ör. IdentityJwtOptionsValidator) ÖNCE çalışır;
        // böylece dosyadan gelen anahtar hem doğrulayıcıya hem imzalama/doğrulama tarafına aynı yoldan ulaşır.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .PostConfigure(jwt =>
            {
                if (string.IsNullOrWhiteSpace(jwt.PrivateKeyPem) && !string.IsNullOrWhiteSpace(jwt.PrivateKeyPemFile))
                    jwt.PrivateKeyPem = ReadPemFile(jwt.PrivateKeyPemFile, "Jwt:PrivateKeyPemFile");
                if (string.IsNullOrWhiteSpace(jwt.PublicKeyPem) && !string.IsNullOrWhiteSpace(jwt.PublicKeyPemFile))
                    jwt.PublicKeyPem = ReadPemFile(jwt.PublicKeyPemFile, "Jwt:PublicKeyPemFile");
            });

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Ayarlar AddJwtBearer(lambda) içinde değil burada: yapılandırma ilk kullanımda okunur, böylece test/ortam
        // ezmeleri (Jwt:PublicKeyPem) servis kaydı sırasından bağımsız çalışır.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtAccessor) =>
            {
                var jwt = jwtAccessor.Value;
                var publicPem = string.IsNullOrWhiteSpace(jwt.PublicKeyPem)
                    ? JwtKeys.LoadEmbeddedPublicKeyPem()
                    : jwt.PublicKeyPem;

                // Token'daki kısa claim adları (sub, role...) olduğu gibi kalsın; eski .NET uzun URI adlarına çevirmesin.
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtKeys.CreatePublicKey(publicPem),
                    RequireSignedTokens = true,
                    // Yalnız RS256: "alg: none" ve HS256 (alg confusion) imzaya bakılmadan reddedilir.
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],

                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,

                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),

                    RoleClaimType = HastaneClaims.Role,
                    NameClaimType = HastaneClaims.Name,
                };

                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse(); // varsayılan boş 401 yerine bizim ProblemDetails gövdemiz
                        return ProblemResponses.WriteUnauthorizedAsync(context.HttpContext,
                            invalidToken: context.AuthenticateFailure is not null);
                    },
                    OnForbidden = context => ProblemResponses.WriteForbiddenAsync(context.HttpContext),
                };
            });

        services.AddHastaneAuthorization();
        return services;
    }

    // Hata iletisinde yalnız ayar adı ve yol yer alır; dosya içeriği (anahtar) asla yazılmaz.
    private static string ReadPemFile(string path, string settingName)
    {
        string pem;
        try
        {
            pem = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"{settingName} ile verilen PEM dosyası okunamadı: {path} ({ex.GetType().Name})", ex);
        }

        if (string.IsNullOrWhiteSpace(pem))
            throw new InvalidOperationException($"{settingName} ile verilen PEM dosyası boş: {path}");
        return pem;
    }
}
