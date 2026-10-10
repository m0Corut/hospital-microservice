using Hastane.WebUI.Clients;
using Hastane.WebUI.ViewModels.Patient;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Patient.Controllers;

public class InvoicesController(IPatientPaymentClient payments, IPublicDoctorClient doctors) : PatientAreaController
{
    private const int PageSize = 10;

    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var result = await payments.ListInvoicesAsync(page, PageSize, ct);
        var list = result.Value!;
        var names = await DoctorNameLookup.LoadAsync(doctors, list.Items.Select(i => i.DoctorId), ct);

        return View(new InvoiceListViewModel
        {
            Page = page,
            TotalPages = Math.Max(1, (list.TotalCount + PageSize - 1) / PageSize),
            Items = list.Items.Select(i => new InvoiceRowViewModel { Invoice = i, DoctorName = names.NameOf(i.DoctorId) }).ToList(),
        });
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var result = await payments.GetInvoiceAsync(id, ct);
        var invoice = result.Value!;
        var names = await DoctorNameLookup.LoadAsync(doctors, [invoice.DoctorId], ct);
        return View(new InvoiceDetailViewModel { Invoice = invoice, DoctorName = names.NameOf(invoice.DoctorId) });
    }

    // Yazdırılabilir görünüm: menüsüz sade sayfa. Yetki/sahiplik Details ile aynı (API: başkasının faturası 404).
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        var result = await payments.GetInvoiceAsync(id, ct);
        var invoice = result.Value!;
        var names = await DoctorNameLookup.LoadAsync(doctors, [invoice.DoctorId], ct);
        return View(new InvoiceDetailViewModel { Invoice = invoice, DoctorName = names.NameOf(invoice.DoctorId) });
    }
}
