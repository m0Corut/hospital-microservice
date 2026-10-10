using System.Text.RegularExpressions;
using Hastane.WebUI.Infrastructure.Gateway;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

// Medya vekili: kullanıcı fotoğrafı gateway'den çekilir ve tarayıcıya WebUI adresiyle (/media/users/{id}/photo) sunulur.
// Anonim: avatar herkese görünür (menü, doktor kartı). Dosya akış olarak geçer, diske yazılmaz.
public sealed partial class MediaController(IGatewayClient gateway) : Controller
{
    private static readonly string[] ImageTypes = ["image/png", "image/jpeg", "image/webp"];

    [HttpGet("media/users/{id}/photo"), AllowAnonymous]
    public async Task<IActionResult> UserPhoto(string id, CancellationToken ct)
    {
        // Kimlik yalnız Guid / kısa harf-rakam-tire: yol ayracı, nokta, ters eğik çizgi içeren değer servise gitmez.
        if (!SafeId().IsMatch(id)) return NotFound();

        var photo = await gateway.GetStreamAsync($"/gateway/users/{Uri.EscapeDataString(id)}/photo", ct);
        var contentType = ImageTypes.FirstOrDefault(t => string.Equals(t, photo.ContentType, StringComparison.OrdinalIgnoreCase));
        if (!photo.IsSuccess || photo.Content is null || contentType is null)
        {
            photo.Dispose();
            return NotFound();
        }

        // Akış, yanıt bittiğinde kapanır (gateway bağlantısı da).
        Response.RegisterForDispose(photo);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'";
        Response.Headers.CacheControl = "public, max-age=60";
        return File(photo.Content, contentType);
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex SafeId();
}
