using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Sayfalı kullanıcı listesi (arama + rol süzgeci) ve satırdan ayrıntıya geçiş. Süzgeç ve sayfa API'de uygulanır.
public class UsersController(IAdminIdentityClient identity, IAdminDoctorClient doctors) : AdminAreaController
{
    private const int PageSize = 20;
    private const int MaxSearchLength = 100; // API'nin sınırı; aşılırsa 400 yerine kırpılır

    [HttpGet]
    public async Task<IActionResult> Index(string? id, string? search, string? role, int page = 1, CancellationToken ct = default)
    {
        // Eski adres biçimi (?id=...) hâlâ doğrudan ayrıntıya götürür.
        if (!string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Details), new { id = id.Trim() });

        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim()[..Math.Min(search.Trim().Length, MaxSearchLength)];
        role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();

        var result = await identity.ListUsersAsync(Math.Max(page, 1), PageSize, search, role, ct);
        if (!result.TryGetValue(out var users))
        {
            // 400 (geçersiz süzgeç) gibi hatada boş liste + mesaj; 401/403/404/503 zaten ortak hata sayfalarına gider.
            ModelState.AddApiProblem(result, "Kullanıcılar listelenemedi.");
            users = new AdminPagedApiModel<AdminUserApiModel>();
        }

        var roles = await identity.GetRolesAsync(ApiFailures.All, ct);
        return View(new UserIndexViewModel
        {
            Search = search,
            Role = role,
            Page = users,
            Roles = (roles.Value ?? []).Select(r => r.Name).ToList(),
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id, CancellationToken ct)
    {
        var userResult = await identity.GetUserAsync(id, ApiFailures.None, ct);
        if (!userResult.TryGetValue(out var user)) return NotFound();

        var roles = await identity.GetRolesAsync(ct);
        var doctorList = await doctors.GetAllAsync(ApiFailures.All, ct);
        var doctorItems = doctorList.Value ?? [];

        return View(new UserDetailsViewModel
        {
            User = user,
            AllRoles = (roles.Value ?? []).Select(r => r.Name).ToList(),
            DoctorChoices = doctorItems.Select(d => new SelectListItem(AdminDisplay.FullName(d), d.Id, d.Id == user.DoctorId)).ToList(),
            LinkedDoctorName = doctorItems.Where(d => d.Id == user.DoctorId).Select(AdminDisplay.FullName).FirstOrDefault(),
        });
    }

    [HttpPost]
    public async Task<IActionResult> AddRole(string id, string? role, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            SetWarning("Bir rol seçin.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await identity.AddRoleAsync(id, role, ct);
        Report(result, $"“{role}” rolü eklendi.", "Rol eklenemedi.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveRole(string id, string role, CancellationToken ct)
    {
        var result = await identity.RemoveRoleAsync(id, role, ct);
        Report(result, $"“{role}” rolü kaldırıldı.", "Rol kaldırılamadı.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> LinkDoctor(string id, string? doctorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doctorId))
        {
            SetWarning("Bir doktor seçin.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await identity.LinkDoctorAsync(id, doctorId, ct);
        Report(result, "Kullanıcı doktor kaydına bağlandı.", "Doktor bağlanamadı.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // Doktor bağını kaldırır (DELETE doctor-link). Yalnız POST; sayfadaki onay penceresinden sonra gönderilir.
    [HttpPost]
    public async Task<IActionResult> UnlinkDoctor(string id, CancellationToken ct)
    {
        var result = await identity.UnlinkDoctorAsync(id, ct);
        Report(result, "Doktor bağı kaldırıldı.", "Doktor bağı kaldırılamadı.");
        return RedirectToAction(nameof(Details), new { id });
    }

    // Başarıda yeşil bilgi; 404/409/400'de API'nin Türkçe Detail mesajı (ör. son Admin, doktor başka kullanıcıya bağlı).
    private void Report(ApiResult result, string success, string fallback)
    {
        if (result.IsSuccess) SetSuccess(success);
        else SetWarning(AdminDisplay.ProblemText(result, fallback));
    }
}
