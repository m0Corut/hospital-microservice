using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Hastane.WebUI.ViewModels.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

// Sosyal Medya: yalnız SocialLinks düzenlenir. Çalışma Saatleri ile aynı sebeple kayıt anında tüm kayıt okunur,
// SocialLinks değiştirilir, kalanı aynen PUT edilir. Kimlik yalnız token'dandır.
public class SocialController(IDoctorPanelProfileClient profile) : DoctorPanelController
{
    private const int MaxLength = 300; // servis: DoctorRules.MaxSocialLength

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var doctor = (await profile.GetAsync(DoctorId, ct)).Value!;
        return View(new SocialFormViewModel { SocialLinks = ProfileFormViewModel.From(doctor).SocialLinks });
    }

    [HttpPost]
    public async Task<IActionResult> Index(SocialFormViewModel model, CancellationToken ct)
    {
        model.SocialLinks ??= new SocialLinksForm();
        var links = model.SocialLinks;
        links.Facebook = Clean(links.Facebook, nameof(links.Facebook));
        links.X = Clean(links.X, nameof(links.X));
        links.Instagram = Clean(links.Instagram, nameof(links.Instagram));
        links.LinkedIn = Clean(links.LinkedIn, nameof(links.LinkedIn));
        links.Pinterest = Clean(links.Pinterest, nameof(links.Pinterest));
        links.YouTube = Clean(links.YouTube, nameof(links.YouTube));
        if (!ModelState.IsValid) return View(model);

        var doctor = (await profile.GetAsync(DoctorId, ct)).Value!;
        var form = ProfileFormViewModel.From(doctor);
        form.SocialLinks = links;

        var result = await profile.UpdateAsync(DoctorId, form.ToWriteBody(DoctorId), ct);
        if (result.IsSuccess)
        {
            SetSuccess("Sosyal medya bağlantılarınız güncellendi.");
            return RedirectToAction(nameof(Index));
        }

        ModelState.AddApiProblem(result);
        return View(model);
    }

    // Boş -> null (bağlantıyı kaldırır). Dolu ise yalnız mutlak http/https adresi (javascript: vb. reddedilir).
    private string? Clean(string? value, string field)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > MaxLength)
            ModelState.AddModelError($"SocialLinks.{field}", $"Adres en çok {MaxLength} karakter olabilir.");
        else if (UiKit.SafeSocialUrl(value) is null)
            ModelState.AddModelError($"SocialLinks.{field}", "Geçerli bir http:// ya da https:// adresi yazın.");
        return value;
    }
}
