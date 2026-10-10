using Hastane.Shared.Security;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api;
using Hastane.WebUI.Models.Api.Public;
using Hastane.WebUI.ViewModels.Public;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

// Yollar öznitelikle: /Doctors (arama) ve /Doctors/{id} (profil). Varsayılan rota {controller}/{action}/{id}
// "/Doctors/d1" adresini Index'e götürürdü.
public class DoctorsController(
    IPublicDoctorClient doctors, IPublicReviewClient reviews, IBranchClient branches) : AppController
{
    [HttpGet("Doctors"), AllowAnonymous]
    public async Task<IActionResult> Index([FromQuery] DoctorSearchFilter filter, CancellationToken ct)
    {
        var branchList = await branches.GetAllAsync(ApiFailures.All, ct);
        var branchItems = branchList.Value ?? [];

        var cards = new List<DoctorCardViewModel>();
        long total = 0;

        if (ModelState.IsValid)
        {
            var criteria = new DoctorSearchCriteria(
                filter.Q, filter.BranchId, filter.Gender, filter.City, filter.MinFee, filter.MaxFee,
                filter.Date, filter.Sort, filter.Page, DoctorSearchViewModel.PageSize);
            var result = await doctors.SearchAsync(criteria, ct);

            if (result.TryGetValue(out var page))
            {
                total = page.TotalCount;

                // Puan özeti: TEK toplu çağrı (N+1 yok). Review servisi kapalıysa kartlar puansız çizilir.
                var summaries = new Dictionary<string, DoctorRatingSummaryApiModel>();
                if (page.Items.Count > 0)
                {
                    var rated = await reviews.GetSummariesAsync(page.Items.Select(d => d.Id), ApiFailures.All, ct);
                    if (rated.Value is not null)
                        foreach (var s in rated.Value) summaries[s.DoctorId] = s;
                }

                cards = page.Items
                    .Select(d => new DoctorCardViewModel
                    {
                        Doctor = d,
                        Rating = summaries.GetValueOrDefault(d.Id),
                        BranchName = branchItems.FirstOrDefault(b => b.Id == d.BranchId)?.Name,
                    })
                    .ToList();
            }
            else
            {
                ModelState.AddApiProblem(result, "Arama yapılamadı.");
            }
        }

        return View(new DoctorSearchViewModel
        {
            Filter = filter,
            Branches = branchItems,
            Doctors = cards,
            TotalCount = total,
        });
    }

    // Ayrıntı ucu (telefon, e-posta) giriş ister; anonim ziyaretçi burada girişe yönlendirilir, API'ye gitmeden.
    [HttpGet("Doctors/{id}"), Authorize]
    public async Task<IActionResult> Details(string id, CancellationToken ct)
        => View(await BuildProfileAsync(id, new ReviewFormModel { Rating = 5 }, ct));

    [HttpPost("Doctors/{id}/Reviews"), Authorize(Roles = HastaneRoles.Patient)]
    public async Task<IActionResult> CreateReview(
        string id, [Bind(Prefix = nameof(DoctorProfileViewModel.ReviewForm))] ReviewFormModel form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await reviews.CreateAsync(new CreateReviewRequest(id, form.Rating, form.Comment?.Trim()), ct);
            if (result.IsSuccess)
            {
                SetSuccess("Yorumunuz yayımlandı.");
                return RedirectToAction(nameof(Details), new { id });
            }

            ModelState.AddApiProblem(result, "Yorum kaydedilemedi.");
        }

        return View("Details", await BuildProfileAsync(id, form, ct));
    }

    [HttpPost("Doctors/{id}/Reviews/{reviewId}/Update"), Authorize(Roles = HastaneRoles.Patient)]
    public async Task<IActionResult> UpdateReview(
        string id, string reviewId, [Bind(Prefix = "EditForm")] ReviewFormModel form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            SetWarning(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m))
                       ?? "Yorum güncellenemedi.");
            return RedirectToAction(nameof(Details), new { id });
        }

        // 404 ekranda ele alınır: başkasının yorumu ya da silinmiş yorum ile aynı cevap (API varlığı sızdırmaz).
        var result = await reviews.UpdateAsync(new UpdateReviewRequest(reviewId, form.Rating, form.Comment?.Trim()), ct);
        if (result.IsSuccess) SetSuccess("Yorumunuz güncellendi.");
        else SetWarning(FailureMessage(result, "Yorum bulunamadı ya da size ait değil."));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("Doctors/{id}/Reviews/{reviewId}/Delete"), Authorize(Roles = HastaneRoles.Patient)]
    public async Task<IActionResult> DeleteReview(string id, string reviewId, CancellationToken ct)
    {
        var result = await reviews.DeleteAsync(reviewId, ct);
        if (result.IsSuccess) SetSuccess("Yorumunuz silindi.");
        else SetWarning(FailureMessage(result, "Yorum bulunamadı ya da size ait değil."));
        return RedirectToAction(nameof(Details), new { id });
    }

    private static string FailureMessage(ApiResult result, string notFound)
        => result.Kind == ApiResultKind.NotFound ? notFound : result.Problem?.Detail ?? "İşlem tamamlanamadı.";

    private async Task<DoctorProfileViewModel> BuildProfileAsync(string id, ReviewFormModel form, CancellationToken ct)
    {
        // Doktor yoksa 404 sayfası (varsayılan). Puan/yorum/branş yardımcı bilgi: kapalıysa profil yine açılır.
        var doctorResult = await doctors.GetAsync(id, ct);
        var doctor = doctorResult.Value!;

        var summary = await reviews.GetSummaryAsync(id, ApiFailures.All, ct);
        var reviewList = await reviews.GetByDoctorAsync(id, ApiFailures.All, ct);
        var branchList = await branches.GetAllAsync(ApiFailures.All, ct);

        var me = User.Identity?.Name;
        return new DoctorProfileViewModel
        {
            Doctor = doctor,
            BranchName = branchList.Value?.FirstOrDefault(b => b.Id == doctor.BranchId)?.Name,
            Summary = summary.Value,
            Reviews = (reviewList.Value ?? [])
                .Select(r => new ReviewRowViewModel
                {
                    Review = r,
                    LooksOwned = me is not null && string.Equals(r.ReviewerName, me, StringComparison.Ordinal),
                })
                .ToList(),
            ReviewsUnavailable = !reviewList.IsSuccess,
            IsPatient = User.IsInRole(HastaneRoles.Patient),
            ReviewForm = form,
        };
    }
}
