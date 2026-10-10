using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Admin;
using Hastane.WebUI.ViewModels.Public;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Analitik: Kafka'daki doktor etkinlik akışından Analytics servisinin ürettiği toplamlar (en çok görüntülenen doktorlar,
// en çok aranan terimler) + akışın durumu + "Yeniden hesapla". Analytics servisi ya da Kafka kapalıyken sayfa yine açılır.
public class AnalyticsController(IAdminAnalyticsClient analytics, IPublicDoctorClient doctors) : AdminAreaController
{
    private static readonly int[] AllowedDays = [1, 7, 30, 90];

    [HttpGet]
    public async Task<IActionResult> Index(int days = 7, CancellationToken ct = default)
    {
        if (!AllowedDays.Contains(days)) days = 7;

        var status = await analytics.StatusAsync(ct);
        if (!status.TryGetValue(out var statusValue))
            return View(new AnalyticsViewModel { Days = days }); // servis kapalı: boş sayfa + uyarı

        var viewed = await analytics.TopViewedAsync(days, 10, ct);
        var terms = await analytics.TopTermsAsync(days, 20, ct);

        var rows = new List<AnalyticsDoctorRow>();
        if (viewed.TryGetValue(out var topViewed) && topViewed.Count > 0)
        {
            // Analytics doktor adını bilmez: tek toplu çağrı (N+1 yok). Doctor servisi kapalıysa adlar genel görünür.
            var batch = await doctors.GetBatchAsync(topViewed.Select(v => v.DoctorId), ApiFailures.All, ct);
            var names = (batch.Value ?? [])
                .GroupBy(d => d.Id)
                .ToDictionary(g => g.Key, g => DisplayText.DoctorName(g.First().Title, g.First().FirstName, g.First().LastName));

            var max = Math.Max(1, topViewed.Max(v => v.Views));
            foreach (var v in topViewed)
                rows.Add(new AnalyticsDoctorRow(v.DoctorId, names.GetValueOrDefault(v.DoctorId, "Bilinmeyen doktor"), v.Views, (int)Math.Max(2, v.Views * 100 / max)));
        }

        return View(new AnalyticsViewModel
        {
            Days = days,
            Status = statusValue,
            Doctors = viewed.IsSuccess ? rows : null,
            Terms = terms.Value,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Rebuild(CancellationToken ct)
    {
        var result = await analytics.RebuildAsync(ct);
        if (result.TryGetValue(out var done))
            SetSuccess($"Yeniden hesaplama başladı: {done.PartitionsRewound} bölüm başa sarıldı, {done.AggregatesDeleted} toplam kaydı silindi. Sayılar birkaç saniyede yeniden dolar.");
        else if (result.Kind == ApiResultKind.Conflict)
            SetWarning(result.Problem?.Detail ?? result.Problem?.Title ?? "Yeniden hesaplama şu an yapılamıyor.");
        else if (result.Kind == ApiResultKind.Unavailable)
            SetWarning("Analitik servisine ulaşılamadı.");
        else
            SetWarning("Yeniden hesaplama başlatılamadı.");
        return RedirectToAction(nameof(Index));
    }
}
