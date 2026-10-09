using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Home;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

public class HomeController(IBranchClient branches) : AppController
{
    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Ana sayfa servis kapalıyken de açılmalı: tüm altyapı hatalarını biz ele alırız, liste yerine uyarı gösteririz.
        var result = await branches.GetAllAsync(ApiFailures.All, ct);
        return View(new HomeIndexViewModel
        {
            Branches = result.Value ?? [],
            BranchesUnavailable = !result.IsSuccess,
        });
    }
}
