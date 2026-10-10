using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Doktor başvuruları: sayfalı liste (durum süzgeci), onay ve red. Onay yalnızca başvuruyu karara bağlar; doktor KAYDI ve
// kullanıcıya bağlama mevcut Doktorlar / Kullanıcılar ekranlarından ayrıca yapılır (onaydan sonra bağlantıları gösteririz).
public class DoctorApplicationsController(IAdminDoctorApplicationClient applications, IBranchClient branches) : AdminAreaController
{
    private const int PageSize = 20;
    private const int MaxNoteLength = 500; // API'nin sınırı

    [HttpGet]
    public async Task<IActionResult> Index(string? status, int page = 1, CancellationToken ct = default)
    {
        status = NormalizeStatus(status);

        var result = await applications.ListAsync(status, Math.Max(page, 1), PageSize, ApiFailures.None, ct);
        if (!result.TryGetValue(out var list))
        {
            // 400 gibi hatada boş liste + mesaj; 401/403/404/503 ortak hata sayfalarına gider.
            ModelState.AddApiProblem(result, "Başvurular listelenemedi.");
            list = new AdminPagedApiModel<Models.Api.DoctorApplicationApiModel>();
        }

        // Branş adı yardımcı bilgidir: branş servisi kapalıysa kimlik gösterilir, sayfa açılır.
        var branchList = await branches.GetAllAsync(ApiFailures.All, ct);

        return View(new DoctorApplicationIndexViewModel
        {
            Status = status,
            Page = list,
            BranchNames = (branchList.Value ?? []).ToDictionary(b => b.Id, b => b.Name, StringComparer.Ordinal),
            ApprovedUserId = TempData["ApprovedUserId"] as string,
            ApprovedName = TempData["ApprovedName"] as string,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Approve(Guid id, string? status, int page = 1, CancellationToken ct = default)
    {
        var result = await applications.ApproveAsync(id, ct);
        if (result.TryGetValue(out var approved))
        {
            SetSuccess("Başvuru onaylandı.");
            TempData["ApprovedUserId"] = approved.UserId;
            TempData["ApprovedName"] = $"{approved.FirstName} {approved.LastName}".Trim();
        }
        else
        {
            ReportFailure(result, "Başvuru onaylanamadı.");
        }

        return RedirectToAction(nameof(Index), new { status = NormalizeStatus(status), page });
    }

    [HttpPost]
    public async Task<IActionResult> Reject(Guid id, string? note, string? status, int page = 1, CancellationToken ct = default)
    {
        note = note?.Trim();
        if (string.IsNullOrEmpty(note))
        {
            SetWarning("Reddetmek için bir not yazın.");
        }
        else if (note.Length > MaxNoteLength)
        {
            SetWarning($"Red notu en çok {MaxNoteLength} karakter olabilir.");
        }
        else
        {
            var result = await applications.RejectAsync(id, note, ct);
            if (result.IsSuccess) SetSuccess("Başvuru reddedildi.");
            else ReportFailure(result, "Başvuru reddedilemedi.");
        }

        return RedirectToAction(nameof(Index), new { status = NormalizeStatus(status), page });
    }

    // Yalnız bilinen üç durum süzgeç olur; başka değer sorguya taşınmaz.
    private static string? NormalizeStatus(string? status) => status?.Trim() switch
    {
        "Pending" => "Pending",
        "Approved" => "Approved",
        "Rejected" => "Rejected",
        _ => null,
    };

    private void ReportFailure(ApiResult result, string fallback)
    {
        if (result.Kind == ApiResultKind.Conflict) SetWarning("Bu başvuru için zaten karar verilmiş.");
        else if (result.Kind == ApiResultKind.NotFound) SetWarning("Başvuru bulunamadı.");
        else SetWarning(AdminDisplay.ProblemText(result, fallback));
    }
}
