using Hastane.WebUI.Clients;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Faturalar salt okunur (fatura ödeme oluşunca backend'de üretilir).
public class InvoicesController(IAdminPaymentClient payments, IAdminDoctorClient doctors, IAdminBranchClient branches) : AdminAreaController
{
    private const int PageSize = 10;

    [HttpGet]
    public async Task<IActionResult> Index(InvoiceFilterViewModel filter, int page = 1, CancellationToken ct = default)
    {
        var result = await payments.GetInvoicesAsync(filter.PatientId, filter.DoctorId, Math.Max(page, 1), PageSize, ct);

        return View(new InvoiceIndexViewModel
        {
            Filter = filter,
            Page = result.Value ?? new(),
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var result = await payments.GetInvoiceAsync(id, ct);
        if (!result.TryGetValue(out var invoice)) return NotFound();

        return View(new InvoiceDetailsViewModel
        {
            Invoice = invoice,
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }
}
