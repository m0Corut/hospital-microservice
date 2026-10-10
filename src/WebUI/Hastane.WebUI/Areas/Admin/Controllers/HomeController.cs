using Hastane.WebUI.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

public class HomeController(IAdminSummaryClient summary) : AdminAreaController
{
    // Sınıf düzeyindeki alan kapısı (Admin) geçerli; action'a ek etiket gerekmez.
    public async Task<IActionResult> Index(CancellationToken ct) => View(await summary.LoadAsync(ct));
}
