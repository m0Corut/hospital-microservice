using System.Text.RegularExpressions;
using Hastane.Shared.Security;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Orders;
using Hastane.WebUI.ViewModels.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

// /Checkout: sepeti siparişe çevirir. Yalnız hasta. Ödeme: kapıda ödeme (gerçek ödeme yok).
[Authorize(Roles = HastaneRoles.Patient)]
public partial class CheckoutController(ICheckoutClient checkout, IPatientProfileClient profile) : AppController
{
    // Sipariş sayfaları önbelleğe alınmaz (geri tuşu / paylaşılan bilgisayar).
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Anahtar form her açıldığında BİR kez üretilir, gizli alanla taşınır: çift tıklama aynı anahtarı yollar.
        var model = new CheckoutViewModel { IdempotencyKey = NewKey() };
        await LoadCartAsync(model, ct);
        if (!model.CartUnavailable && model.Cart.Items.Count > 0) await PrefillAddressAsync(model, ct);
        return View(model);
    }

    [HttpPost, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CheckoutViewModel model, CancellationToken ct)
    {
        if (!IdempotencyKeyFormat().IsMatch(model.IdempotencyKey ?? string.Empty))
            model.IdempotencyKey = NewKey(); // bozuk/eksik anahtar: yeni niyet

        if (!ModelState.IsValid)
        {
            await LoadCartAsync(model, ct);
            return View(model);
        }

        // Sepet boş mu, fiyat ne: formdan DEĞİL, API'den (Order servisi sepeti ve ürün fiyatlarını kendisi okur).
        // Sepeti burada önceden okuyup "boşsa dur" DEMİYORUZ: çift tıklamanın ikinci isteğinde sepet zaten boşalmıştır
        // ve API aynı anahtarla ilk siparişi geri verir; ön kontrol bunu bozardı.
        var request = new CheckoutRequest(model.ShippingAddress!.Trim(), string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim());
        var result = await checkout.PlaceOrderAsync(model.IdempotencyKey!, request, ApiFailures.Unavailable, ct);

        if (result.TryGetValue(out var order))
        {
            SetSuccess($"Siparişiniz alındı. Sipariş numaranız: {order.OrderNumber}");
            return RedirectToAction("Details", "Orders", new { area = "Patient", id = order.Id });
        }

        switch (result.Kind)
        {
            case ApiResultKind.ValidationFailed:
                // Boş sepet ya da geçersiz alan: sunucu kaydetmedi.
                ModelState.AddApiProblem(result, "Sipariş verilemedi.");
                break;
            case ApiResultKind.Conflict:
                // Ürün satıştan kalktı / stok yetersiz: bu anahtar kapandı (stok yetersizse iptal edilmiş bir sipariş kaydı var).
                ModelState.AddApiProblem(result, "Sipariş tamamlanamadı.");
                model.IdempotencyKey = NewKey();
                break;
            case ApiResultKind.Unavailable:
                // Market/gateway yanıt vermedi. Sunucu bu anahtarı "iptal edilmiş sipariş" olarak saklamış olabilir;
                // aynı anahtarla yeniden denemek hep aynı hatayı döndürürdü, bu yüzden yeni anahtar.
                // Çift sipariş riski yok: önceki deneme başarılıysa sepet boşalmıştır.
                ModelState.AddModelError(string.Empty,
                    "Sipariş şu anda işlenemedi. Sepetiniz korunuyor; biraz sonra tekrar deneyin ya da 'Siparişlerim' sayfasından durumu kontrol edin.");
                model.IdempotencyKey = NewKey();
                break;
            default:
                ModelState.AddApiProblem(result, "Sipariş tamamlanamadı.");
                break;
        }

        await LoadCartAsync(model, ct);
        return View(model);
    }

    private async Task LoadCartAsync(CheckoutViewModel model, CancellationToken ct)
    {
        var cart = await checkout.GetCartAsync(ApiFailures.Unavailable, ct);
        if (cart.TryGetValue(out var value)) model.Cart = value;
        else model.CartUnavailable = true;
    }

    // Hasta profilinde adres varsa forma ön dolu gelir (kullanıcı değiştirebilir). Profil servisi kapalıysa/boşsa form boş açılır.
    private async Task PrefillAddressAsync(CheckoutViewModel model, CancellationToken ct)
    {
        var result = await profile.GetMineAsync(ApiFailures.All, ct);
        if (!result.TryGetValue(out var p)) return;

        var address = string.Join(", ", new[] { p.Address, p.City }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        if (address.Length == 0) return;

        model.ShippingAddress = address.Length > 300 ? address[..300] : address;
        model.AddressPrefilled = true;
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");

    [GeneratedRegex("^[A-Za-z0-9_-]{16,100}$")]
    private static partial Regex IdempotencyKeyFormat();
}
