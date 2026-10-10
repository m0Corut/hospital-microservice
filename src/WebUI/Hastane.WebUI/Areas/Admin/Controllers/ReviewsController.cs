using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Tüm yorumlar. API yalnız doktora göre süzer; puan ve metin araması burada, bellekte yapılır (liste küçük, tek istek).
public class ReviewsController(IAdminReviewClient reviews, IAdminDoctorClient doctors) : AdminAreaController
{
    private const int PageSize = 20;

    [HttpGet]
    public async Task<IActionResult> Index(string? doctorId, int? rating, string? search, int page = 1, CancellationToken ct = default)
    {
        doctorId = string.IsNullOrWhiteSpace(doctorId) ? null : doctorId.Trim();
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (rating is < 1 or > 5) rating = null;

        var result = await reviews.GetAllAsync(doctorId, ct);
        var all = result.Value ?? [];
        var doctorList = (await doctors.GetAllAsync(ApiFailures.All, ct)).Value ?? [];

        IEnumerable<AdminReviewApiModel> filtered = all;
        if (rating is { } r) filtered = filtered.Where(x => x.Rating == r);
        if (search is not null)
            filtered = filtered.Where(x => Contains(x.Comment, search) || Contains(x.ReviewerName, search));
        var rows = filtered.OrderByDescending(x => x.CreatedAt).ToList();

        var pageNo = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(rows.Count / (double)PageSize)));
        return View(new ReviewIndexViewModel
        {
            DoctorId = doctorId,
            Rating = rating,
            Search = search,
            Page = pageNo,
            PageSize = PageSize,
            TotalCount = rows.Count,
            Items = rows.Skip((pageNo - 1) * PageSize).Take(PageSize).ToList(),
            DoctorNames = doctorList.ToDictionary(d => d.Id, AdminDisplay.FullName),
        });
    }

    // Yalnız POST (+ antiforgery: global filtre). Silme sonrası aynı süzgeçle listeye dönülür.
    [HttpPost]
    public async Task<IActionResult> Delete(string id, string? doctorId, int? rating, string? search, int page = 1, CancellationToken ct = default)
    {
        var result = await reviews.DeleteAsync(id, ct);
        if (result.IsSuccess) SetSuccess("Yorum silindi.");
        else if (result.Kind == ApiResultKind.NotFound) SetWarning("Yorum bulunamadı (zaten silinmiş olabilir).");
        else SetWarning(AdminDisplay.ProblemText(result, "Yorum silinemedi."));
        return RedirectToAction(nameof(Index), new { doctorId, rating, search, page });
    }

    private static bool Contains(string? text, string term)
        => text is not null && text.Contains(term, StringComparison.CurrentCultureIgnoreCase);
}
