using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Public;
using Hastane.WebUI.ViewModels.Public;
using Hastane.WebUI.ViewModels.Social;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Patient.Controllers;

// Favori doktorlar. Kalp düğmesi (kart ve profil) bu controller'ın Add/Remove POST'larına gider; başarıdan sonra returnUrl'e
// (yalnız yerel yol) döner. Kimlik token'dan: formda hasta kimliği yok.
public class FavoritesController(
    IPatientProfileClient profiles, IPublicDoctorClient doctors, IPublicReviewClient reviews, IBranchClient branches) : PatientAreaController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var favorites = await profiles.GetFavoritesAsync(ApiFailures.None, ct);
        var ordered = (favorites.Value ?? []).OrderByDescending(f => f.CreatedAt).Select(f => f.DoctorId).Distinct(StringComparer.Ordinal).ToList();

        var cards = new List<DoctorCardViewModel>();
        if (ordered.Count > 0)
        {
            // Doktor bilgisi TEK toplu çağrı, puan özeti TEK toplu çağrı (N+1 yok). Biri kapalıysa kartlar eksik bilgiyle çizilir.
            var batch = await doctors.GetBatchAsync(ordered, ApiFailures.All, ct);
            var byId = (batch.Value ?? []).ToDictionary(d => d.Id, StringComparer.Ordinal);

            var ratings = new Dictionary<string, DoctorRatingSummaryApiModel>(StringComparer.Ordinal);
            var rated = await reviews.GetSummariesAsync(ordered, ApiFailures.All, ct);
            if (rated.Value is not null)
                foreach (var s in rated.Value) ratings[s.DoctorId] = s;

            var branchList = (await branches.GetAllAsync(ApiFailures.All, ct)).Value ?? [];

            // Silinmiş doktor (toplu yanıtta yok) için kart çizilmez.
            foreach (var id in ordered.Where(byId.ContainsKey))
            {
                var d = byId[id];
                cards.Add(new DoctorCardViewModel
                {
                    Doctor = new DoctorSearchItemApiModel
                    {
                        Id = d.Id,
                        FirstName = d.FirstName,
                        LastName = d.LastName,
                        Title = d.Title,
                        Degrees = d.Degrees,
                        BranchId = d.BranchId,
                        ImageUrl = d.ImageUrl,
                    },
                    Rating = ratings.GetValueOrDefault(id),
                    BranchName = branchList.FirstOrDefault(b => b.Id == d.BranchId)?.Name,
                    IsFavorite = true,
                });
            }
        }

        return View(new FavoritesViewModel { Doctors = cards });
    }

    [HttpPost]
    public async Task<IActionResult> Add(string doctorId, string? returnUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doctorId)) return Back(returnUrl);

        // 404: doktor yok / 503: Doctor servisi doğrulayamadı. İkisi de ekranda uyarı olarak söylenir.
        var result = await profiles.AddFavoriteAsync(doctorId, ApiFailures.NotFound | ApiFailures.Unavailable, ct);
        if (result.IsSuccess) SetSuccess("Doktor favorilerinize eklendi.");
        else SetWarning(result.Kind == ApiResultKind.NotFound ? "Doktor bulunamadı." : result.Problem?.Detail ?? "Favorilere eklenemedi, lütfen tekrar deneyin.");
        return Back(returnUrl);
    }

    [HttpPost]
    public async Task<IActionResult> Remove(string doctorId, string? returnUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doctorId)) return Back(returnUrl);

        var result = await profiles.RemoveFavoriteAsync(doctorId, ApiFailures.Unavailable, ct);
        if (result.IsSuccess) SetSuccess("Doktor favorilerinizden çıkarıldı.");
        else SetWarning("Favorilerden çıkarılamadı, lütfen tekrar deneyin.");
        return Back(returnUrl);
    }

    private IActionResult Back(string? returnUrl)
        => string.IsNullOrEmpty(returnUrl) ? RedirectToAction(nameof(Index)) : RedirectToLocal(returnUrl);
}
