using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Public;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

public class BranchesController(IBranchClient branches) : AppController
{
    [AllowAnonymous]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Bu sayfanın tek işi branş listesi: servis kapalıysa varsayılan davranış (503 sayfası) doğru.
        var result = await branches.GetAllAsync(ApiFailures.None, ct);
        return View(new BranchesIndexViewModel { Branches = result.Value ?? [] });
    }
}
