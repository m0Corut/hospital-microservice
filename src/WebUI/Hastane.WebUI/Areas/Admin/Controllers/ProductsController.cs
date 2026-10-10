using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Admin;
using Hastane.WebUI.ViewModels.Shop;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Mağaza ürün yönetimi. Yetki: AdminAreaController (alan + Admin rolü). Silme: GET onay sayfasını gösterir, silmeyi yalnız POST yapar.
public class ProductsController(IAdminProductClient products) : AdminAreaController
{
    private const int PageSize = 20;

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? category, int page = 1, CancellationToken ct = default)
    {
        q = ShopDisplay.CleanSearch(q);
        category = ShopDisplay.NormalizeCategory(category);
        page = Math.Clamp(page, 1, 100000);

        var result = await products.ListAsync(q, category, page, PageSize, ct);
        var data = result.Value;

        return View(new AdminProductListViewModel
        {
            Items = data?.Items ?? [],
            Search = q,
            Category = category,
            Pager = new PagerViewModel
            {
                Action = nameof(Index),
                Page = page,
                PageSize = PageSize,
                TotalCount = data?.TotalCount ?? 0,
                Filters = new Dictionary<string, string?> { ["q"] = q, ["category"] = category },
            },
        });
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new ProductFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(ProductFormViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View("Form", model);

        var result = await products.CreateAsync(model.ToBody(), ct);
        if (!result.IsSuccess)
        {
            ModelState.AddApiProblem(result);
            return View("Form", model);
        }

        SetSuccess("Ürün eklendi.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var result = await products.GetAsync(id, ct);
        return result.TryGetValue(out var product) ? View("Form", ProductFormViewModel.From(product)) : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, ProductFormViewModel model, CancellationToken ct)
    {
        model.Id = id;
        if (!ModelState.IsValid) return View("Form", model);

        var result = await products.UpdateAsync(id, model.ToBody(), ct);
        if (!result.IsSuccess)
        {
            // 409: kayıt sırasında bir sipariş stoğu düşürdü; yönetici güncel stoğu görüp yeniden kaydetmeli.
            ModelState.AddApiProblem(result);
            return View("Form", model);
        }

        SetSuccess("Ürün güncellendi.");
        return RedirectToAction(nameof(Index));
    }

    // Onay sayfası: yalnız gösterir, hiçbir şeyi silmez.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await products.GetAsync(id, ct);
        return result.TryGetValue(out var product) ? View(product) : NotFound();
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken ct)
    {
        var result = await products.DeleteAsync(id, ct);
        if (!result.IsSuccess)
        {
            SetWarning(AdminDisplay.ProblemText(result, "Ürün silinemedi."));
            return RedirectToAction(nameof(Index));
        }

        SetSuccess("Ürün silindi.");
        return RedirectToAction(nameof(Index));
    }
}
