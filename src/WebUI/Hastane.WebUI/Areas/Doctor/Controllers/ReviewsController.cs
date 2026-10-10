using Hastane.WebUI.Clients;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

// Salt okuma: yorum yazma/düzenleme/silme Review servisinde Patient/Admin'e ait, doktorda bu uçlar yok.
public class ReviewsController(IDoctorPanelReviewClient reviews) : DoctorPanelController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var summary = (await reviews.SummaryAsync(DoctorId, ct)).Value;
        var list = (await reviews.ListAsync(DoctorId, ct)).Value ?? [];

        return View(new ReviewsViewModel
        {
            ReviewCount = summary?.ReviewCount ?? list.Count,
            AverageRating = summary?.AverageRating ?? 0,
            RecommendationPercent = summary?.RecommendationPercent ?? 0,
            Reviews = list.OrderByDescending(r => r.CreatedAt).ToList(),
        });
    }
}
