using Hastane.WebUI.Clients;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

public class PaymentsController(IAdminPaymentClient payments, IAdminDoctorClient doctors, IAdminBranchClient branches) : AdminAreaController
{
    private const int PageSize = 10;
    private static readonly string[] Statuses = ["Pending", "Paid", "Failed", "Refunded"];

    [HttpGet]
    public async Task<IActionResult> Index(PaymentFilterViewModel filter, int page = 1, CancellationToken ct = default)
    {
        var status = Statuses.Contains(filter.Status) ? filter.Status : null;
        var result = await payments.GetPaymentsAsync(status, filter.PatientId, filter.DoctorId, Math.Max(page, 1), PageSize, ct);

        return View(new PaymentIndexViewModel
        {
            Filter = filter,
            Page = result.Value ?? new(),
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var result = await payments.GetPaymentAsync(id, ct);
        if (!result.TryGetValue(out var payment)) return NotFound();

        return View(new PaymentDetailsViewModel
        {
            Payment = payment,
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }

    // Onay sayfası: yalnız gösterir, para iadesi yapmaz.
    [HttpGet]
    public async Task<IActionResult> Refund(Guid id, CancellationToken ct)
    {
        var result = await payments.GetPaymentAsync(id, ct);
        if (!result.TryGetValue(out var payment)) return NotFound();

        return View(new PaymentDetailsViewModel
        {
            Payment = payment,
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }

    // İade kuralları (yalnız ödenmiş ödeme, çift iade yok) backend'dedir; reddederse (400/409) mesajı gösterilir.
    [HttpPost, ActionName("Refund")]
    public async Task<IActionResult> RefundConfirmed(Guid id, CancellationToken ct)
    {
        var result = await payments.RefundAsync(id, ct);
        if (result.IsSuccess) SetSuccess("Ödeme iade edildi.");
        else SetWarning(AdminDisplay.ProblemText(result, "İade yapılamadı."));

        return RedirectToAction(nameof(Details), new { id });
    }
}
