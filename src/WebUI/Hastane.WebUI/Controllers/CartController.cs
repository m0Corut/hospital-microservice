using Hastane.Shared.Security;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Shop;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

// Sepetim: yalnız Patient. Sepet ve fiyatlar Market servisindedir; burada yalnız gösterilir ve değiştirilir.
[Authorize(Roles = HastaneRoles.Patient)]
[Route("Cart")]
public class CartController(IShopCartClient cart) : AppController
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Sepet servisi (Redis) kapalıysa API 503 verir: varsayılan "Servis şu an yanıt vermiyor" sayfası.
        var result = await cart.GetAsync(ApiFailures.None, ct);
        return View(new CartViewModel { Cart = result.Value! });
    }

    [HttpPost("Update")]
    public async Task<IActionResult> Update(Guid productId, int quantity, CancellationToken ct)
    {
        if (quantity is < 0 or > ShopDisplay.MaxQuantity)
        {
            SetWarning($"Adet 1 ile {ShopDisplay.MaxQuantity} arasında olmalı.");
            return RedirectToAction(nameof(Index));
        }

        var result = await cart.SetItemAsync(productId, quantity, ApiFailures.NotFound, ct);
        if (result.IsSuccess) SetSuccess(quantity == 0 ? "Ürün sepetten çıkarıldı." : "Sepet güncellendi.");
        else if (result.Kind == ApiResultKind.NotFound) SetWarning("Bu ürün artık satışta değil; sepetten çıkarabilirsiniz.");
        else SetWarning(result.Problem?.Detail ?? "Sepet güncellenemedi.");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Remove")]
    public async Task<IActionResult> Remove(Guid productId, CancellationToken ct)
    {
        // Adet 0 = sepetten çıkar (silinmiş ürün de çıkarılabilir).
        var result = await cart.SetItemAsync(productId, 0, ApiFailures.None, ct);
        if (result.IsSuccess) SetSuccess("Ürün sepetten çıkarıldı.");
        else SetWarning(result.Problem?.Detail ?? "Ürün sepetten çıkarılamadı.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var result = await cart.ClearAsync(ct);
        if (result.IsSuccess) SetSuccess("Sepet boşaltıldı.");
        else SetWarning(result.Problem?.Detail ?? "Sepet boşaltılamadı.");
        return RedirectToAction(nameof(Index));
    }
}
