using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Admin;
using Hastane.WebUI.ViewModels.Pharmacy;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Reçete karşılama (Pharmacy servisi). Akış: Reçete Id gir -> önizleme (kalemler + eşleşen ilaç + stok) -> "Karşıla" (onaylı POST).
// Eşleştirme ve stok kuralları API'dedir; burada yalnız sonucu gösteririz.
public class PharmacyDispensingController(IPharmacyDispensingClient dispensing) : AdminAreaController
{
    private const int PageSize = 15;

    [HttpGet]
    public async Task<IActionResult> Index(string? prescriptionId, CancellationToken ct)
    {
        var model = new PharmacyDispenseViewModel { PrescriptionId = prescriptionId?.Trim() };
        if (string.IsNullOrEmpty(model.PrescriptionId)) return View(model);

        if (!Guid.TryParse(model.PrescriptionId, out var id))
        {
            ModelState.AddModelError(nameof(model.PrescriptionId), "Reçete Id geçerli bir kimlik olmalı (ör. 0199a1b2-...).");
            return View(model);
        }

        var result = await dispensing.PreviewAsync(id, ct);
        switch (result.Kind)
        {
            case ApiResultKind.Success:
                model.Preview = result.Value;
                break;
            case ApiResultKind.NotFound:
                ModelState.AddModelError(nameof(model.PrescriptionId), "Bu kimlikle bir reçete bulunamadı.");
                break;
            case ApiResultKind.Unavailable:
                SetWarningNow("Reçete servisine şu an ulaşılamıyor. Biraz sonra tekrar deneyin.");
                break;
            default:
                ModelState.AddApiProblem(result);
                break;
        }

        return View(model);
    }

    // Onaylı POST: önizleme ekranındaki "Karşıla" düğmesi. Hepsi-ya-hiç kuralı API'dedir.
    [HttpPost]
    public async Task<IActionResult> Dispense(string? prescriptionId, CancellationToken ct)
    {
        if (!Guid.TryParse(prescriptionId?.Trim(), out var id))
        {
            SetWarning("Reçete Id geçerli bir kimlik olmalı.");
            return RedirectToAction(nameof(Index));
        }

        var result = await dispensing.DispenseAsync(id, ct);
        switch (result.Kind)
        {
            case ApiResultKind.Success:
                SetSuccess("Reçete karşılandı; stoklar düşüldü.");
                return RedirectToAction(nameof(Dispensations));
            case ApiResultKind.Conflict:
                // 409: eşleşmeyen / stoğu yetmeyen kalem ya da "zaten karşılandı". Hiçbir ilaç düşülmedi.
                // Önizleme ekranı kalem kalem nedenini gösterir (aynı kural, salt okunur).
                SetWarning(AdminDisplay.ProblemText(result, "Reçete karşılanamadı."));
                break;
            case ApiResultKind.NotFound:
                SetWarning("Bu kimlikle bir reçete bulunamadı.");
                break;
            case ApiResultKind.Unavailable:
                SetWarning("Reçete servisine ulaşılamadı; stoğa dokunulmadı. Biraz sonra tekrar deneyin.");
                break;
            default:
                SetWarning(AdminDisplay.ProblemText(result, "Reçete karşılanamadı."));
                break;
        }

        return RedirectToAction(nameof(Index), new { prescriptionId = id });
    }

    [HttpGet]
    public async Task<IActionResult> Dispensations(int page = 1, CancellationToken ct = default)
    {
        var result = await dispensing.GetDispensationsAsync(Math.Max(page, 1), PageSize, ct);
        return View(new PharmacyDispensationsViewModel { Page = result.Value ?? new() });
    }

    // Aynı istekte gösterilecek uyarı (yönlendirme yok): TempData bir sonraki isteğe kalırdı.
    private void SetWarningNow(string message) => ViewData["WarningNow"] = message;
}
