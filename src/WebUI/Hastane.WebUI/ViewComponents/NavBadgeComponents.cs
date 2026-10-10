using Hastane.Shared.Security;
using Hastane.WebUI.Clients;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.ViewComponents;

// Üst menüdeki zil. Sayı her sayfa çiziminde sorulur (istek başına bir kez, bkz. NavCountsClient). Servis kapalıysa ya da
// bu sayfada çağrı anlamsızsa sayı null döner: zil kalır, rozet gizlenir, sayfa bozulmaz.
public class NotificationBellViewComponent(INavCountsClient counts) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!NavBadge.ShouldQuery(HttpContext, UserClaimsPrincipal)) return Content(string.Empty);
        return View(await counts.UnreadNotificationsAsync(HttpContext.RequestAborted));
    }
}

// "Mesajlar" öğelerinin yanındaki okunmamış rozeti (üst menü, açılır menü, kenar menü: üçü de aynı sayıyı kullanır).
// Yalnız Patient / Doctor için; Admin'e sorulmaz (servis ona 403 verir).
public class MessageBadgeViewComponent(INavCountsClient counts) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!NavBadge.ShouldQuery(HttpContext, UserClaimsPrincipal)) return Content(string.Empty);
        if (!UserClaimsPrincipal.IsInRole(HastaneRoles.Patient) && !UserClaimsPrincipal.IsInRole(HastaneRoles.Doctor))
            return Content(string.Empty);

        return View(await counts.UnreadMessagesAsync(HttpContext.RequestAborted));
    }
}

internal static class NavBadge
{
    // Anonimde ve hata sayfalarında (403/404/503 yeniden çizimi) gateway'e gidilmez: hata sayfası ek çağrıyla yavaşlamaz.
    public static bool ShouldQuery(HttpContext http, System.Security.Claims.ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true
           && !string.Equals(http.Request.RouteValues["controller"]?.ToString(), "Error", StringComparison.OrdinalIgnoreCase);

    // Rozet metni: 0 gizli, 99'dan sonrası "99+".
    public static string? Text(long? count) => count is > 0 ? (count > 99 ? "99+" : count.ToString()) : null;
}
