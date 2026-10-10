using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Admin;
using Hastane.WebUI.ViewModels.Orders;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Patient.Controllers;

// /Patient/Orders: hastanın kendi siparişleri. Sahiplik API'dedir (başkasının siparişi 404 sayfası).
public class OrdersController(IPatientOrderClient orders) : PatientAreaController
{
    private const int PageSize = 10;

    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var result = await orders.ListAsync(page, PageSize, ct);
        var list = result.Value!;

        return View(new PatientOrderListViewModel
        {
            Page = page,
            TotalPages = Math.Max(1, (list.TotalCount + PageSize - 1) / PageSize),
            Items = list.Items,
        });
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var order = (await orders.GetAsync(id, ct)).Value!;
        return View(new OrderDetailViewModel { Order = order, CanCancel = OrderDisplay.PatientCanCancel(order.Status) });
    }

    // Hasta yalnız "Alındı" durumundaki siparişi iptal eder; kural API'de, reddederse (409) mesajı gösterilir.
    [HttpPost]
    public async Task<IActionResult> Cancel(Guid id, CancelOrderForm form, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            SetWarning("İptal nedeni en fazla 300 karakter olabilir.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await orders.CancelAsync(id, form.Reason, ct);
        if (result.IsSuccess) SetSuccess("Siparişiniz iptal edildi.");
        else SetWarning(AdminDisplay.ProblemText(result, "Sipariş iptal edilemedi."));

        return RedirectToAction(nameof(Details), new { id });
    }
}
