using Hastane.WebUI.Clients;
using Hastane.WebUI.Models.Api.Patient;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Hastane.WebUI.ViewModels.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

// Faturalarım: GET invoices (servis doktora yalnız kendi faturalarını verir: kapsam token'daki doctor_id'den zorlanır).
public class InvoicesController(IDoctorPanelPaymentClient payments) : DoctorPanelController
{
    private const int PageSize = 10;

    // Özet için en çok bu kadar sayfa (50'şer) okunur. Daha fazla ödeme varsa özet GÖSTERİLMEZ (tahmin yok).
    private const int SummaryPageSize = 50;
    private const int SummaryMaxPages = 10;

    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var list = (await payments.ListInvoicesAsync(page, PageSize, ct)).Value!;

        var paid = await LoadAllPaymentsAsync("Paid", ct);
        var refunded = await LoadAllPaymentsAsync("Refunded", ct);

        var refundedIds = refunded?.Select(p => p.Id).ToHashSet() ?? [];
        var patientLabel = await PatientLabelsAsync(list.Items.Select(i => i.PatientId), ct);

        string? note = null;
        DoctorEarningsSummary? summary = null;
        if (paid is null)
            note = "Kazanç özeti şu anda hesaplanamadı (ödeme kayıtları alınamadı ya da çok fazla).";
        else
            summary = BuildSummary(paid, refunded?.Count ?? 0);

        return View(new DoctorInvoiceListViewModel
        {
            Page = page,
            TotalCount = list.TotalCount,
            TotalPages = Math.Max(1, (list.TotalCount + PageSize - 1) / PageSize),
            Items = list.Items.Select(i => new DoctorInvoiceRow
            {
                Invoice = i,
                PatientLabel = patientLabel(i.PatientId),
                Refunded = refundedIds.Contains(i.PaymentId),
            }).ToList(),
            Summary = summary,
            SummaryNote = note,
        });
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
        => View(await BuildDetailAsync(id, ct));

    // Yazdırılabilir görünüm: menüsüz sade sayfa. Sahiplik Details ile aynı (başkasının faturası API'de 404).
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
        => View(await BuildDetailAsync(id, ct));

    private async Task<DoctorInvoiceDetailViewModel> BuildDetailAsync(Guid id, CancellationToken ct)
    {
        var invoice = (await payments.GetInvoiceAsync(id, ct)).Value!;
        var patientLabel = await PatientLabelsAsync([invoice.PatientId], ct);
        var payment = await payments.GetPaymentAsync(invoice.PaymentId, ct);
        return new DoctorInvoiceDetailViewModel
        {
            Invoice = invoice,
            PatientLabel = patientLabel(invoice.PatientId),
            Refunded = payment.Value?.Status == "Refunded",
        };
    }

    // Verilen durumdaki TÜM ödemeler (kendi ödemeleri; kapsam serviste). Alınamadıysa ya da sınırı aştıysa null.
    private async Task<List<PaymentApiModel>?> LoadAllPaymentsAsync(string status, CancellationToken ct)
    {
        var all = new List<PaymentApiModel>();
        for (var page = 1; page <= SummaryMaxPages; page++)
        {
            var result = await payments.ListPaymentsAsync(status, page, SummaryPageSize, ct);
            if (!result.TryGetValue(out var chunk)) return null;
            all.AddRange(chunk.Items);
            if (all.Count >= chunk.TotalCount) return all;
            if (chunk.Items.Count == 0) return null;
        }

        return null;
    }

    private static DoctorEarningsSummary BuildSummary(List<PaymentApiModel> paid, int refundedCount)
    {
        var now = AppointmentTime.NowLocal();
        static List<MoneyTotal> Sum(IEnumerable<PaymentApiModel> items) => items
            .GroupBy(p => p.Currency)
            .Select(g => new MoneyTotal(g.Key, g.Sum(p => p.Amount)))
            .OrderBy(t => t.Currency, StringComparer.Ordinal)
            .ToList();

        var thisMonth = paid.Where(p =>
        {
            var local = AppointmentTime.ToClinicLocal(p.PaidAt ?? p.CreatedAt);
            return local.Year == now.Year && local.Month == now.Month;
        });

        return new DoctorEarningsSummary
        {
            PaidCount = paid.Count,
            Total = Sum(paid),
            ThisMonth = Sum(thisMonth),
            RefundedCount = refundedCount,
        };
    }
}
