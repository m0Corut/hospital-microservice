using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Social;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Patient.Controllers;

// /Patient/Profile: tam hasta profili (doğum tarihi, kan grubu, adres, acil durum kişisi). Hesap bilgisi (ad, e-posta)
// /Account/Profile'dadır; bu sayfa Patient servisinin tıbbi olmayan kişisel profilidir. Kimlik token'dan: formda UserId yok.
public class ProfileController(IPatientProfileClient profiles) : PatientAreaController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Kayıt hiç yoksa servis boş bir profil döner (404 değil); form boş açılır.
        var result = await profiles.GetMineAsync(ApiFailures.None, ct);
        return View(PatientProfileFormViewModel.From(result.Value!));
    }

    [HttpPost]
    public async Task<IActionResult> Index(PatientProfileFormViewModel model, CancellationToken ct)
    {
        // Age formdan gelmez; sunucu hesaplar. Hata durumunda yeniden çizilen formda gösterilmez.
        model.Age = null;
        if (!ModelState.IsValid) return View(model);

        var result = await profiles.UpdateMineAsync(model.ToRequest(), ct);
        if (!result.IsSuccess)
        {
            ModelState.AddApiProblem(result, "Profil kaydedilemedi.");
            return View(model);
        }

        SetSuccess("Profil bilgileriniz kaydedildi.");
        return RedirectToAction(nameof(Index));
    }
}
