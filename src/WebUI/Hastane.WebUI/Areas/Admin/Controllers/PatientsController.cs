using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Hasta rolündeki kullanıcıların sayfalı listesi: GET users?role=Patient. Satırda randevu sayısı YOK (hasta başına istek = N+1);
// yalnız o hastanın randevularına giden bağlantı var.
public class PatientsController(IAdminIdentityClient identity) : AdminAreaController
{
    private const int PageSize = 20;
    private const int MaxSearchLength = 100;
    private const string PatientRole = "Patient";

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken ct = default)
    {
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim()[..Math.Min(search.Trim().Length, MaxSearchLength)];

        var result = await identity.ListUsersAsync(Math.Max(page, 1), PageSize, search, PatientRole, ct);
        if (!result.TryGetValue(out var patients))
        {
            ModelState.AddApiProblem(result, "Hastalar listelenemedi.");
            patients = new AdminPagedApiModel<AdminUserApiModel>();
        }

        return View(new PatientIndexViewModel { Search = search, Page = patients });
    }
}
