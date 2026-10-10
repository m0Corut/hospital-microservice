using Hastane.Shared.Security;
using Hastane.WebUI.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.ViewComponents;

// Üst menüdeki sepet simgesi + adet rozeti. Yalnız Patient için çizilir (sepet yalnız hastanın). Kullanım:
//   @await Component.InvokeAsync("CartBadge")
// Servis kapalıysa sayı null döner: simge kalır, rozet gizlenir, sayfa bozulmaz.
public class CartBadgeViewComponent(IShopCartCountClient counts) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!NavBadge.ShouldQuery(HttpContext, UserClaimsPrincipal)) return Content(string.Empty);
        if (!UserClaimsPrincipal.IsInRole(HastaneRoles.Patient)) return Content(string.Empty);

        return View(await counts.CountAsync(HttpContext.RequestAborted));
    }
}
