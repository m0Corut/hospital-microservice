using Hastane.WebUI.Clients;
using Hastane.WebUI.ViewModels.Admin;
using Hastane.WebUI.ViewModels.Orders;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// /Admin/Orders: tüm siparişler (durum süzgeci), ayrıntı, durum değiştirme, iptal. Kurallar (durum makinesi) API'dedir.
public class OrdersController(IAdminOrderClient orders) : AdminAreaController
{
    private const int PageSize = 10;

    [HttpGet]
    public async Task<IActionResult> Index(AdminOrderFilterViewModel filter, int page = 1, CancellationToken ct = default)
    {
        // Bilinmeyen durum değeri API'ye hiç gitmez (400 yerine "Tümü").
        var status = OrderDisplay.Statuses.Contains(filter.Status) ? filter.Status : null;
        filter.Status = status;
        var result = await orders.ListAsync(status, Math.Max(page, 1), PageSize, ct);

        return View(new AdminOrderIndexViewModel { Filter = filter, Page = result.Value! });
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var order = (await orders.GetAsync(id, ct)).Value!;
        return View(new AdminOrderDetailViewModel
        {
            Order = order,
            NextStatuses = OrderDisplay.NextStatuses(order.Status),
            CanCancel = OrderDisplay.AdminCanCancel(order.Status),
        });
    }

    // Durum değiştirme: yalnız POST. Geçersiz geçiş API'de 409 olur ve mesaj gösterilir.
    [HttpPost]
    public async Task<IActionResult> UpdateStatus(Guid id, string status, CancellationToken ct)
    {
        if (!OrderDisplay.Statuses.Contains(status) || status == "Cancelled")
        {
            SetWarning("Geçersiz durum.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await orders.UpdateStatusAsync(id, status, ct);
        if (result.IsSuccess) SetSuccess($"Sipariş durumu '{OrderDisplay.Status(status)}' olarak güncellendi.");
        else SetWarning(AdminDisplay.ProblemText(result, "Durum güncellenemedi."));

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(Guid id, CancelOrderForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            SetWarning("İptal nedeni en fazla 300 karakter olabilir.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await orders.CancelAsync(id, form.Reason, ct);
        if (result.IsSuccess)
        {
            // Stok iadesi Market'e ulaşmadıysa sipariş yine iptaldir; yönetici bilsin.
            if (result.Value is { StockReleasePending: true })
                SetWarning("Sipariş iptal edildi ancak stok iadesi Market servisine iletilemedi (iade bekliyor).");
            else
                SetSuccess("Sipariş iptal edildi.");
        }
        else
        {
            SetWarning(AdminDisplay.ProblemText(result, "Sipariş iptal edilemedi."));
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
