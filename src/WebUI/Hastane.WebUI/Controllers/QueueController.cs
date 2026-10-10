using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Public;
using Hastane.WebUI.ViewModels.Queue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hastane.WebUI.Controllers;

// Bekleme salonu çağrı ekranı: herkese açık, giriş istemez (duvardaki TV oturum açmaz).
// Gösterilen şey zaten salonda söylenen bilgidir: numara + maskeli ad ("Can Y."). Hasta/randevu kimliği hiçbir yerde yok.
[AllowAnonymous]
public class QueueController(
    IQueueClient queue, IPublicDoctorClient doctors, IOptions<GatewayOptions> gateway, IConfiguration configuration) : AppController
{
    private const int MaxDoctorIdLength = 64;
    private const int DoctorListSize = 50; // Doctor arama ucunun üst sınırı

    [HttpGet]
    public async Task<IActionResult> Display(string? doctorId, CancellationToken ct)
    {
        // Ekran önbelleğe alınmasın: bayat "şu an çağrılan" yanıltıcı olur.
        Response.Headers.CacheControl = "no-store";
        var id = Clean(doctorId);

        // Doktor listesi seçim kutusu içindir; Doctor servisi kapalıysa ekran yine de (seçili doktorla) açılır.
        var list = await doctors.SearchAsync(new DoctorSearchCriteria(null, null, null, null, null, null, null, "name", 1, DoctorListSize), ApiFailures.All, ct);
        var options = (list.Value?.Items ?? [])
            .Select(d => new DoctorOption(d.Id, DisplayText.DoctorName(d.Title, d.FirstName, d.LastName)))
            .ToList();

        string? doctorName = null;
        Models.Api.Queue.QueueDisplayApiModel? display = null;
        var unavailable = false;
        if (id is not null)
        {
            var summary = await doctors.GetBatchAsync([id], ApiFailures.All, ct);
            var found = summary.Value?.FirstOrDefault(d => d.Id == id);
            doctorName = found is null ? null : DisplayText.DoctorName(found.Title, found.FirstName, found.LastName);
            if (doctorName is not null && options.All(o => o.Id != id)) options.Insert(0, new DoctorOption(id, doctorName));

            var result = await queue.DisplayAsync(id, ct);
            display = result.Value;
            unavailable = !result.IsSuccess;
        }

        return View(new QueueDisplayViewModel
        {
            DoctorId = id,
            DoctorName = doctorName,
            Doctors = options,
            Display = display,
            Unavailable = unavailable,
            HubUrl = HubUrl(),
            PollUrl = id is null ? string.Empty : Url.Action(nameof(DisplayData), "Queue", new { area = "", doctorId = id })!,
        });
    }

    // Tarayıcı JS'inin 15 sn'lik yedek yenilemesi ve olay sonrası tazelemesi. Tarayıcı Gateway'e değil kendi sitemize konuşur
    // (çerez/CORS derdi yok); WebUI Gateway'den anonim okur.
    [HttpGet]
    public async Task<IActionResult> DisplayData(string? doctorId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var id = Clean(doctorId);
        if (id is null) return BadRequest(new { error = "doctorId zorunludur." });

        var result = await queue.DisplayAsync(id, ct);
        if (!result.IsSuccess || result.Value is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Sıra bilgisi şu an alınamıyor." });

        return Json(result.Value);
    }

    private string? Clean(string? doctorId)
        => string.IsNullOrWhiteSpace(doctorId) || doctorId.Length > MaxDoctorIdLength ? null : doctorId.Trim();

    private string HubUrl() => QueueText.HubUrl(gateway.Value.BaseUrl, configuration["Gateway:PublicBaseUrl"]);
}
