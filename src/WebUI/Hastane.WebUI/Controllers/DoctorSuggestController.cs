using Hastane.WebUI.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

// Doktor arama kutusunun <datalist> önerisi için küçük JSON ucu (tarayıcı gateway'e gitmez; token tarayıcıya verilmiyor).
// Yanıt yalnız metin listesidir (en çok 8): doktor adları ve hizmet adları.
[AllowAnonymous]
[Route("doctor-suggest")]
public class DoctorSuggestController(IPublicDoctorSuggestClient suggestions) : AppController
{
    private const int MaxQueryLength = 100;

    [HttpGet]
    public async Task<IActionResult> Get(string? q, CancellationToken ct)
    {
        q = q?.Trim();
        if (string.IsNullOrEmpty(q) || q.Length > MaxQueryLength) return Json(Array.Empty<string>());
        var list = await suggestions.SuggestAsync(q, ct);
        return Json(list.Select(s => s.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(8).ToArray());
    }
}
