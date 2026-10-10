using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Pharmacy;
using Hastane.WebUI.ViewModels.Admin;
using Hastane.WebUI.ViewModels.Pharmacy;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// İlaç kataloğu ve stok yönetimi (Pharmacy servisi). Yetki: AdminAreaController (alan + Admin rolü).
// İnce controller: client çağırır, ViewModel kurar. Kurallar (stok eksiye düşmez, barkod tekil...) API'dedir; 400/409 formda gösterilir.
public class PharmacyMedicinesController(IPharmacyMedicineClient medicines) : AdminAreaController
{
    private const int PageSize = 15;

    [HttpGet]
    public async Task<IActionResult> Index(PharmacyMedicineFilterViewModel filter, int page = 1, CancellationToken ct = default)
    {
        var form = PharmacyDisplay.Forms.Contains(filter.Form) ? filter.Form : null;
        var result = await medicines.GetPageAsync(filter.Search, form, Math.Max(page, 1), PageSize, ct);

        return View(new PharmacyMedicineIndexViewModel
        {
            Filter = new PharmacyMedicineFilterViewModel { Search = filter.Search, Form = form },
            Page = result.Value ?? new(),
        });
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new PharmacyMedicineFormViewModel());

    [HttpPost]
    public async Task<IActionResult> Create(PharmacyMedicineFormViewModel model, CancellationToken ct)
    {
        var body = ToBody(model, includeStock: true);
        if (!ModelState.IsValid || body is null) return View("Form", model);

        var result = await medicines.CreateAsync(body, ct);
        if (!result.IsSuccess)
        {
            ModelState.AddApiProblem(result);
            return View("Form", model);
        }

        SetSuccess("İlaç eklendi.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var result = await medicines.GetAsync(id, ct);
        return View("Form", PharmacyMedicineFormViewModel.From(result.Value!));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, PharmacyMedicineFormViewModel model, CancellationToken ct)
    {
        model.Id = id;
        var body = ToBody(model, includeStock: false);
        if (!ModelState.IsValid || body is null) return View("Form", model);

        var result = await medicines.UpdateAsync(id, body, ct);
        if (!result.IsSuccess)
        {
            // 409 (barkod çakışması ya da başkası arada değiştirdi) ve 400 mesajları formun üstünde görünür.
            ModelState.AddApiProblem(result);
            return View("Form", model);
        }

        SetSuccess("İlaç güncellendi.");
        return RedirectToAction(nameof(Index));
    }

    // Onay sayfası: yalnız gösterir, hiçbir şeyi silmez.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await medicines.GetAsync(id, ct);
        return View(result.Value!);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken ct)
    {
        var result = await medicines.DeleteAsync(id, ct);
        if (result.IsSuccess) SetSuccess("İlaç silindi (kayıtlar korunur, kataloğtan kalktı).");
        else SetWarning(AdminDisplay.ProblemText(result, "İlaç silinemedi."));

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Stock(Guid id, CancellationToken ct)
    {
        var medicine = (await medicines.GetAsync(id, ct)).Value!;
        return View(new PharmacyStockViewModel { Id = medicine.Id, MedicineName = medicine.Name, CurrentStock = medicine.Stock });
    }

    [HttpPost]
    public async Task<IActionResult> Stock(Guid id, PharmacyStockViewModel model, CancellationToken ct)
    {
        model.Id = id;
        if (!ModelState.IsValid || model.Delta is null or 0)
        {
            if (model.Delta == 0) ModelState.AddModelError(nameof(model.Delta), "Değişim 0 olamaz.");
            return await ReloadStockViewAsync(model, ct);
        }

        var result = await medicines.AdjustStockAsync(id, model.Delta.Value, model.Reason!.Trim(), ct);
        if (!result.IsSuccess)
        {
            // Stok eksiye düşecekse 409: "Stok eksiye düşemez" mesajı formda kalır.
            ModelState.AddApiProblem(result);
            return await ReloadStockViewAsync(model, ct);
        }

        SetSuccess("Stok güncellendi.");
        return RedirectToAction(nameof(Movements), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Movements(Guid id, int page = 1, CancellationToken ct = default)
    {
        var medicine = (await medicines.GetAsync(id, ct)).Value!;
        var movements = await medicines.GetMovementsAsync(id, Math.Max(page, 1), PageSize, ct);

        return View(new PharmacyMovementsViewModel { Medicine = medicine, Page = movements.Value ?? new() });
    }

    // Hatalı formda ekranın "mevcut stok / ilaç adı" bilgisi yeniden okunur (form alanları bunları göndermez).
    private async Task<IActionResult> ReloadStockViewAsync(PharmacyStockViewModel model, CancellationToken ct)
    {
        var medicine = (await medicines.GetAsync(model.Id, ct)).Value!;
        model.MedicineName = medicine.Name;
        model.CurrentStock = medicine.Stock;
        return View("Stock", model);
    }

    // Fiyat metni geçersizse alan hatası ekler ve null döner.
    private PharmacyMedicineWriteBody? ToBody(PharmacyMedicineFormViewModel model, bool includeStock)
    {
        var price = PharmacyDisplay.ParsePrice(model.Price);
        if (price is null)
        {
            if (!string.IsNullOrWhiteSpace(model.Price)) ModelState.AddModelError(nameof(model.Price), "Fiyat geçerli bir sayı olmalı (ör. 25,50).");
            return null;
        }

        if (!ModelState.IsValid) return null;

        return new PharmacyMedicineWriteBody
        {
            Name = model.Name!.Trim(),
            ActiveIngredient = model.ActiveIngredient!.Trim(),
            Form = model.Form!,
            Strength = model.Strength!.Trim(),
            Barcode = model.Barcode!.Trim(),
            Price = price.Value,
            Stock = includeStock ? model.Stock : null,
            MinStock = model.MinStock,
            RequiresPrescription = model.RequiresPrescription,
            RowVersion = includeStock ? null : model.RowVersion,
        };
    }
}
