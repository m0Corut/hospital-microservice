using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Public;
using Hastane.WebUI.ViewModels.Home;
using Hastane.WebUI.ViewModels.Public;
using Hastane.WebUI.ViewModels.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

public class HomeController(
    IBranchClient branches, IPublicDoctorClient doctors, IPublicReviewClient reviews, IPatientProfileClient patientProfile) : AppController
{
    private const int FeaturedCount = 6;

    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Ana sayfa servis kapalıyken de açılmalı: tüm altyapı hatalarını biz ele alırız, liste yerine uyarı gösteririz.
        var result = await branches.GetAllAsync(ApiFailures.All, ct);
        var branchList = result.Value ?? [];

        // Öne çıkan doktorlar: aramanın ilk sayfası (varsayılan sıra). Puan özeti TEK toplu çağrı; kapalıysa kartlar puansız.
        var search = await doctors.SearchAsync(new DoctorSearchCriteria(null, null, null, null, null, null, null, null, 1, FeaturedCount), ApiFailures.All, ct);
        var featured = new List<DoctorCardViewModel>();
        if (search.TryGetValue(out var page) && page.Items.Count > 0)
        {
            var summaries = new Dictionary<string, DoctorRatingSummaryApiModel>();
            var rated = await reviews.GetSummariesAsync(page.Items.Select(d => d.Id), ApiFailures.All, ct);
            if (rated.Value is not null)
                foreach (var s in rated.Value) summaries[s.DoctorId] = s;

            var favoriteIds = await FavoriteLookup.LoadAsync(User, patientProfile, ct);
            featured = page.Items.Select(d => new DoctorCardViewModel
            {
                Doctor = d,
                IsFavorite = favoriteIds.Contains(d.Id),
                Rating = summaries.GetValueOrDefault(d.Id),
                BranchName = branchList.FirstOrDefault(b => b.Id == d.BranchId)?.Name,
            }).ToList();
        }

        return View(new HomeIndexViewModel
        {
            Branches = branchList,
            BranchesUnavailable = !result.IsSuccess,
            FeaturedDoctors = featured,
            FeaturedUnavailable = !search.IsSuccess,
        });
    }

    // ---------- Genel sayfalar (eğitim projesi, kurgusal içerik; gateway çağrısı YOK) ----------

    [AllowAnonymous]
    public IActionResult About() => View();

    [AllowAnonymous]
    public IActionResult Faq() => View();

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Contact() => View(new ContactFormViewModel());

    // Form bilerek HİÇBİR YERE gönderilmez: e-posta servisi, gateway çağrısı ve kayıt yok. Antiforgery globaldir.
    [AllowAnonymous]
    [HttpPost]
    public IActionResult Contact(ContactFormViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        SetSuccess("Mesajınız alındı (eğitim projesi, kaydedilmez).");
        return RedirectToAction(nameof(Contact));
    }
}
