using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

// Tüm controller'ların tabanı. Ortak yardımcılar buraya; yetki etiketi BURADA YOK (her controller/alan kendi koyar).
public abstract class AppController : Controller
{
    // returnUrl yalnız yerel adres olabilir; aksi halde (https://evil.com, //evil.com) ana sayfa. Open redirect yok.
    protected IActionResult RedirectToLocal(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Home", new { area = "" });

    // Bir sonraki sayfada gösterilecek kısa bilgi/uyarı (_Layout okur).
    protected void SetSuccess(string message) => TempData["Success"] = message;

    protected void SetWarning(string message) => TempData["Warning"] = message;
}
