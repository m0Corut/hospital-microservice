using System.Security.Cryptography;
using System.Text;
using Hastane.Market.Api.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Hastane.Market.Api.Security;

// Servisler arası iç uçların kapısı: X-Internal-Key başlığı ayardaki Internal:ApiKey ile eşleşmeli.
// Ayar boşsa HER ZAMAN 401 ("anahtar tanımlı değil = herkese açık" olmaz). Bu uçlar JWT istemez, [AllowAnonymous] ile
// işaretlenir ve bu filtre onların tek kapısıdır.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Internal-Key";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var configured = context.HttpContext.RequestServices.GetRequiredService<IOptions<InternalOptions>>().Value.ApiKey;
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (!KeysMatch(configured, provided))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Kimlik doğrulanamadı",
                Detail = "Geçerli bir iç servis anahtarı gerekli.",
            })
            { StatusCode = StatusCodes.Status401Unauthorized };
        }
        return Task.CompletedTask;
    }

    // İki değer de SHA-256 ile sabit uzunluğa getirilir, sonra sabit zamanlı karşılaştırılır:
    // yanıt süresi "kaç karakter doğru" bilgisini sızdırmaz (uzunluk farkı da dahil).
    public static bool KeysMatch(string? configured, string? provided)
    {
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrEmpty(provided)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
