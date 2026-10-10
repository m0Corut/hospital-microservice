using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

public class RolesController(IAdminIdentityClient identity) : AdminAreaController
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await BuildAsync(new RoleCreateViewModel(), ct));

    [HttpPost]
    public async Task<IActionResult> Create(RoleCreateViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await identity.CreateRoleAsync(model.Name!.Trim(), ct);
            if (result.IsSuccess)
            {
                SetSuccess("Rol oluşturuldu.");
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddApiProblem(result);
        }

        return View("Index", await BuildAsync(model, ct));
    }

    private async Task<RoleIndexViewModel> BuildAsync(RoleCreateViewModel form, CancellationToken ct)
    {
        var roles = await identity.GetRolesAsync(ct);
        return new RoleIndexViewModel { Roles = roles.Value ?? [], NewRole = form };
    }
}
