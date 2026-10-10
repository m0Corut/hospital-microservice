using System.Globalization;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.ViewModels.Shared;

namespace Hastane.WebUI.ViewModels.Admin;

// Sayfaya <script type="application/json"> içinde gömülen grafik verisi. Alan null ise o grafik "veri alınamadı" der.
public sealed class DashboardChartPayload
{
    public ChartSeries? Daily { get; init; }
    public ChartSeries? Status { get; init; }
    public RevenueSeries? Revenue { get; init; }
    public ChartSeries? DoctorsByBranch { get; init; }
}

public sealed class ChartSeries
{
    public List<string> Labels { get; init; } = [];
    public List<int> Values { get; init; } = [];
    // Durum grafiğinde İngilizce durum anahtarları (renk eşlemesi için); diğerlerinde boş.
    public List<string> Keys { get; init; } = [];
}

public sealed class RevenueSeries
{
    public List<string> Labels { get; init; } = [];
    public List<decimal> Values { get; init; } = [];
    public string Currency { get; init; } = "TRY";
}

public sealed record DashboardOtherCurrency(string Currency, decimal Total);

public sealed record DashboardAppointmentRow(Guid Id, string Doctor, string Branch, string When, string Status, string StatusText, string Badge);

public sealed record DashboardPaymentRow(Guid Id, string Doctor, string Amount, string When, string Status, string StatusText, string Badge);

public sealed class AdminDashboardData
{
    public DashboardChartPayload Charts { get; init; } = new();
    public List<DashboardOtherCurrency> OtherCurrencies { get; init; } = [];
    public bool PaymentsTruncated { get; init; }
    public List<DashboardAppointmentRow>? RecentAppointments { get; init; }
    public List<DashboardPaymentRow>? RecentPayments { get; init; }
}

public sealed class AdminHomeViewModel
{
    public required AdminSummary Summary { get; init; }
    public required AdminDashboardData Dashboard { get; init; }
}

// Toplama mantığı (saf, test edilebilir). Gün ve ay gruplaması İSTANBUL takvimine göredir (AppointmentTime), sunucu saat dilimine değil.
public static class AdminDashboardBuilder
{
    public const int RevenueMonths = 6;
    public const int RecentCount = 5;
    private static readonly string[] StatusOrder = ["Pending", "Confirmed", "Completed", "Cancelled"];
    private static readonly string[] MonthNames = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];

    public static AdminDashboardData Build(AdminDashboardRaw raw)
    {
        var doctorNames = (raw.Doctors ?? []).ToDictionary(d => d.Id, AdminDisplay.FullName, StringComparer.Ordinal);
        var branchNames = (raw.Branches ?? []).ToDictionary(b => b.Id, b => b.Name, StringComparer.Ordinal);
        string Doctor(string id) => doctorNames.GetValueOrDefault(id, id);
        string Branch(string id) => branchNames.GetValueOrDefault(id, id);

        var revenue = BuildRevenue(raw.Payments, raw.NowUtc, out var others);
        var truncated = raw.Payments is { Count: >= AdminDashboardClient.MaxPayments } && raw.PaymentTotal > raw.Payments.Count;

        return new AdminDashboardData
        {
            Charts = new DashboardChartPayload
            {
                Daily = BuildDaily(raw.Appointments, raw.NowUtc),
                Status = BuildStatus(raw.Appointments),
                Revenue = revenue,
                DoctorsByBranch = BuildDoctorsByBranch(raw.Doctors, raw.Branches),
            },
            OtherCurrencies = others,
            PaymentsTruncated = truncated,
            RecentAppointments = raw.Appointments?
                .OrderByDescending(a => a.CreatedAt).Take(RecentCount)
                .Select(a => new DashboardAppointmentRow(a.Id, Doctor(a.DoctorId), Branch(a.BranchId), AdminDisplay.Date(a.AppointmentDate),
                    a.Status, AdminDisplay.AppointmentStatus(a.Status), AdminDisplay.StatusBadge(a.Status)))
                .ToList(),
            RecentPayments = raw.Payments?
                .OrderByDescending(p => p.CreatedAt).Take(RecentCount)
                .Select(p => new DashboardPaymentRow(p.Id, Doctor(p.DoctorId), AdminDisplay.Money(p.Amount, p.Currency), AdminDisplay.Date(p.CreatedAt),
                    p.Status, AdminDisplay.PaymentStatus(p.Status), AdminDisplay.StatusBadge(p.Status)))
                .ToList(),
        };
    }

    // Son 30 İstanbul günü (bugün dahil), her gün için randevu sayısı; randevusuz gün 0.
    public static ChartSeries? BuildDaily(IReadOnlyList<AdminAppointmentApiModel>? appointments, DateTime nowUtc)
    {
        if (appointments is null) return null;
        var today = AppointmentTime.ToClinicLocal(nowUtc).Date;
        var perDay = appointments.GroupBy(a => AppointmentTime.ToClinicLocal(a.AppointmentDate).Date).ToDictionary(g => g.Key, g => g.Count());
        var series = new ChartSeries();
        for (var i = AdminDashboardClient.WindowDays - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            series.Labels.Add(day.ToString("dd.MM", CultureInfo.InvariantCulture));
            series.Values.Add(perDay.GetValueOrDefault(day));
        }

        return series;
    }

    // Durum dağılımı: bilinen dört durum sabit sırada (yalnız sayısı > 0 olanlar), bilinmeyen durum sonda kendi adıyla.
    public static ChartSeries? BuildStatus(IReadOnlyList<AdminAppointmentApiModel>? appointments)
    {
        if (appointments is null) return null;
        var counts = appointments.GroupBy(a => a.Status).ToDictionary(g => g.Key, g => g.Count());
        var series = new ChartSeries();
        foreach (var key in StatusOrder.Concat(counts.Keys.Except(StatusOrder).Order(StringComparer.Ordinal)))
        {
            var count = counts.GetValueOrDefault(key);
            if (count <= 0) continue;
            series.Keys.Add(key);
            series.Labels.Add(AdminDisplay.AppointmentStatus(key));
            series.Values.Add(count);
        }

        return series;
    }

    // Aylık gelir: yalnız Paid. İade edilen ödeme Refunded durumuna geçtiği için Paid toplamına girmez (net gelir).
    // Ödeme tarihi PaidAt (yoksa CreatedAt), İstanbul ayına göre. TRY grafiğe girer; diğer para birimleri ayrı toplanır.
    public static RevenueSeries? BuildRevenue(IReadOnlyList<AdminPaymentApiModel>? payments, DateTime nowUtc, out List<DashboardOtherCurrency> others)
    {
        others = [];
        if (payments is null) return null;

        var nowLocal = AppointmentTime.ToClinicLocal(nowUtc);
        var thisMonth = new DateTime(nowLocal.Year, nowLocal.Month, 1);
        var months = Enumerable.Range(0, RevenueMonths).Select(i => thisMonth.AddMonths(-(RevenueMonths - 1 - i))).ToList();
        var totals = months.ToDictionary(m => m, _ => 0m);
        var foreign = new SortedDictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var p in payments.Where(p => p.Status == "Paid"))
        {
            var local = AppointmentTime.ToClinicLocal(p.PaidAt ?? p.CreatedAt);
            var month = new DateTime(local.Year, local.Month, 1);
            if (month < months[0] || month > thisMonth) continue;

            var currency = p.Currency.ToUpperInvariant();
            if (currency == "TRY") totals[month] += p.Amount;
            else foreign[currency] = foreign.GetValueOrDefault(currency) + p.Amount;
        }

        others = foreign.Select(kv => new DashboardOtherCurrency(kv.Key, kv.Value)).ToList();
        return new RevenueSeries
        {
            Labels = months.Select(m => $"{MonthNames[m.Month - 1]} {m.Year}").ToList(),
            Values = months.Select(m => totals[m]).ToList(),
        };
    }

    // Branş başına doktor sayısı (çoktan aza). Branş listesi varsa doktoru olmayan branşlar 0 ile görünür.
    public static ChartSeries? BuildDoctorsByBranch(IReadOnlyList<AdminDoctorListItemApiModel>? doctors, IReadOnlyList<AdminBranchApiModel>? branches)
    {
        if (doctors is null) return null;
        var counts = doctors.GroupBy(d => d.BranchId).ToDictionary(g => g.Key, g => g.Count());
        var names = (branches ?? []).ToDictionary(b => b.Id, b => b.Name, StringComparer.Ordinal);
        var rows = names.Keys.Union(counts.Keys)
            .Select(k => (Name: names.GetValueOrDefault(k, k), Count: counts.GetValueOrDefault(k)))
            .OrderByDescending(r => r.Count).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        return new ChartSeries { Labels = rows.Select(r => r.Name).ToList(), Values = rows.Select(r => r.Count).ToList() };
    }
}
