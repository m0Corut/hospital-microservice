using Hastane.WebUI.Clients;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

public class HomeController(IAdminSummaryClient summary, IAdminDashboardClient dashboard) : AdminAreaController
{
    // Sınıf düzeyindeki alan kapısı (Admin) geçerli; action'a ek etiket gerekmez.
    // Sayı kutuları ve grafik verisi birbirinden bağımsız çekilir; ikisi de servis kapalıyken null alanlarla döner (sayfa açılır).
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var summaryTask = summary.LoadAsync(ct);
        var rawTask = dashboard.LoadAsync(ct);
        await Task.WhenAll(summaryTask, rawTask);

        return View(new AdminHomeViewModel
        {
            Summary = await summaryTask,
            Dashboard = AdminDashboardBuilder.Build(await rawTask),
        });
    }
}
