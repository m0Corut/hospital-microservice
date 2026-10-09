using Hastane.WebUI.ViewModels.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

// UseStatusCodePagesWithReExecute("/Error/{0}") ve UseExceptionHandler("/Error/500") buraya gelir.
// Yanıt durum kodu zaten ayarlıdır (403, 404, 500, 503); bu action yalnız içeriği çizer.
// Yeniden yürütülen istek POST olabilir: antiforgery doğrulaması burada yok, yoksa hata sayfasının kendisi 400 verir.
[AllowAnonymous, IgnoreAntiforgeryToken]
public class ErrorController : Controller
{
    [Route("Error/{code:int}")]
    public IActionResult Index(int code)
    {
        var model = code switch
        {
            403 => new ErrorPageViewModel(403, "Yetkiniz yok", "Bu sayfayı görüntülemek için gerekli yetkiye sahip değilsiniz."),
            404 => new ErrorPageViewModel(404, "Sayfa bulunamadı", "Aradığınız sayfa ya da kayıt bulunamadı."),
            503 => new ErrorPageViewModel(503, "Servis şu an yanıt vermiyor", "Bir servise şu anda ulaşamıyoruz. Lütfen biraz sonra tekrar deneyin."),
            _ => new ErrorPageViewModel(code, "Bir şeyler ters gitti", "Beklenmeyen bir hata oluştu. Sorun devam ederse lütfen bizimle iletişime geçin."),
        };

        // Hata ayrıntısı (özel durum, yığın izi) bilerek yok: yalnız günlüğe yazılır.
        model = model with { RequestId = HttpContext.TraceIdentifier };
        return View(model);
    }
}
