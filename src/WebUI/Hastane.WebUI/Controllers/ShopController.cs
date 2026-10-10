using Hastane.Shared.Security;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Shop;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

// Mağaza vitrini herkese açık. Satın alma (sepete ekleme) yalnız Patient rolü; giriş yoksa girişe yönlendirilir.
// Yollar öznitelikle: varsayılan {controller}/{action}/{id} "/Shop/{guid}" adresini Index'e götürürdü.
public class ShopController(IShopProductClient products, IShopCartClient cart) : AppController
{
    [HttpGet("Shop"), AllowAnonymous]
    public async Task<IActionResult> Index([FromQuery] ShopFilter filter, CancellationToken ct)
    {
        // Süzgeç değerleri güvenli sınırlara çekilir: bilinmeyen kategori yok sayılır, sayfa 1'den küçük olamaz.
        filter.Q = ShopDisplay.CleanSearch(filter.Q);
        filter.Category = ShopDisplay.NormalizeCategory(filter.Category);
        filter.Page = Math.Clamp(filter.Page, 1, 100000);

        var result = await products.SearchAsync(filter.Q, filter.Category, filter.Page, ShopDisplay.PageSize, ct);
        var page = result.Value;

        return View(new ShopIndexViewModel
        {
            Filter = filter,
            Items = page?.Items ?? [],
            TotalCount = page?.TotalCount ?? 0,
            CanBuy = User.IsInRole(HastaneRoles.Patient),
            IsSignedIn = User.Identity?.IsAuthenticated == true,
        });
    }

    [HttpGet("Shop/{id:guid}"), AllowAnonymous]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        // Ürün yok ya da pasif: API 404 verir, varsayılan 404 sayfası çıkar.
        var result = await products.GetAsync(id, ct);
        return View(new ProductDetailsViewModel
        {
            Product = result.Value!,
            CanBuy = User.IsInRole(HastaneRoles.Patient),
            IsSignedIn = User.Identity?.IsAuthenticated == true,
        });
    }

    // Sepete ekle = mevcut adede ekle. Market API adedi MUTLAK değer olarak aldığından önce sepet okunur.
    [HttpPost("Shop/{id:guid}/AddToCart"), Authorize(Roles = HastaneRoles.Patient)]
    public async Task<IActionResult> AddToCart(Guid id, int quantity, string? returnUrl, CancellationToken ct)
    {
        quantity = Math.Clamp(quantity, 1, ShopDisplay.MaxQuantity);

        var current = await cart.GetAsync(ApiFailures.None, ct);
        var existing = current.Value?.Items.FirstOrDefault(i => i.ProductId == id && i.IsAvailable)?.Quantity ?? 0;
        var target = Math.Min(existing + quantity, ShopDisplay.MaxQuantity);

        if (target == existing)
        {
            SetWarning($"Sepetinizde bu üründen zaten en fazla ({ShopDisplay.MaxQuantity}) adet var.");
            return RedirectBack(id, returnUrl);
        }

        // 404 (ürün satıştan kalktı) bu ekranda ele alınır: hata sayfası yerine uyarı.
        var result = await cart.SetItemAsync(id, target, ApiFailures.NotFound, ct);
        if (result.IsSuccess)
            SetSuccess(target == existing + quantity ? "Ürün sepete eklendi." : $"Sepet adedi en fazla {ShopDisplay.MaxQuantity} olabilir; {target} adede ayarlandı.");
        else if (result.Kind == ApiResultKind.NotFound)
            SetWarning("Bu ürün artık satışta değil.");
        else
            SetWarning(result.Problem?.Detail ?? "Ürün sepete eklenemedi.");

        return RedirectBack(id, returnUrl);
    }

    // returnUrl yalnız yerel adres olabilir (open redirect yok); yoksa ürün sayfasına döner.
    private IActionResult RedirectBack(Guid id, string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Details), new { id });
}
