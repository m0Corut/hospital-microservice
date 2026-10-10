using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Arama dizini: durum + "Yeniden oluştur". Dizin Elasticsearch'te tutulan türetilmiş bir kopyadır (kaynak: MongoDB);
// doktor kaydı sırasında dizin yazımı başarısız olduysa tutarsızlık buradan görülür ve giderilir.
public class SearchIndexController(IAdminDoctorSearchIndexClient index) : AdminAreaController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await index.StatusAsync(ct);
        return View(new DoctorSearchIndexViewModel { Status = result.IsSuccess ? result.Value : null });
    }

    [HttpPost]
    public async Task<IActionResult> Rebuild(CancellationToken ct)
    {
        var result = await index.RebuildAsync(ct);
        if (result.TryGetValue(out var rebuilt)) SetSuccess($"Arama dizini yeniden oluşturuldu: {rebuilt.Indexed} doktor yazıldı.");
        else if (result.Kind == ApiResultKind.Conflict) SetWarning("Arama dizini kapalı (Elasticsearch:Url ayarlı değil).");
        else if (result.Kind == ApiResultKind.Unavailable || result.StatusCode == 503) SetWarning("Arama dizinine ulaşılamadı. Elasticsearch çalışıyor mu?");
        else SetWarning("Arama dizini yeniden oluşturulamadı.");
        return RedirectToAction(nameof(Index));
    }
}
