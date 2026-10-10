using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Identity "tüm kullanıcıları listele" ucu sunmaz: kullanıcı yalnız Id ile bulunur (GET /users/{id}).
public class UsersController(IAdminIdentityClient identity, IAdminDoctorClient doctors) : AdminAreaController
{
    [HttpGet]
    public IActionResult Index(string? id)
        => string.IsNullOrWhiteSpace(id) ? View() : RedirectToAction(nameof(Details), new { id = id.Trim() });

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

    // Başarıda yeşil bilgi; 404/409/400'de API'nin Türkçe Detail mesajı (ör. son Admin, doktor başka kullanıcıya bağlı).
    private void Report(ApiResult result, string success, string fallback)
    {
        if (result.IsSuccess) SetSuccess(success);
        else SetWarning(AdminDisplay.ProblemText(result, fallback));
    }
}
