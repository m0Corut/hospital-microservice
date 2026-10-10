using System.Globalization;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.ViewModels.Admin;

// Görünümde kullanılan ortak biçimlendirme: API UTC döner, ekranda yerel saat, biçim dd.MM.yyyy HH:mm.
public static class AdminDisplay
{
    public static string Date(DateTime? value)
        => value is null ? "-" : value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string Money(decimal amount, string currency)
        => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";

    public static string AppointmentStatus(string status) => status switch
    {
        "Pending" => "Beklemede",
        "Confirmed" => "Onaylandı",
        "Cancelled" => "İptal edildi",
        "Completed" => "Tamamlandı",
        _ => status,
    };

    public static string PaymentStatus(string status) => status switch
    {
        "Pending" => "Beklemede",
        "Paid" => "Ödendi",
        "Failed" => "Başarısız",
        "Refunded" => "İade edildi",
        _ => status,
    };

    public static string StatusBadge(string status) => status switch
    {
        "Confirmed" or "Paid" or "Completed" => "text-bg-success",
        "Cancelled" or "Failed" => "text-bg-danger",
        "Refunded" => "text-bg-secondary",
        _ => "text-bg-warning",
    };

    public static string Gender(AdminDoctorGender gender) => gender switch
    {
        AdminDoctorGender.Male => "Erkek",
        AdminDoctorGender.Female => "Kadın",
        _ => "Diğer",
    };

    public static string Day(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Pazartesi",
        DayOfWeek.Tuesday => "Salı",
        DayOfWeek.Wednesday => "Çarşamba",
        DayOfWeek.Thursday => "Perşembe",
        DayOfWeek.Friday => "Cuma",
        DayOfWeek.Saturday => "Cumartesi",
        _ => "Pazar",
    };

    public static string FullName(AdminDoctorListItemApiModel d)
        => string.Join(' ', new[] { d.Title, d.FirstName, d.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    // Rol çıkarma/doktor bağlama gibi hata kodlarında API'nin Türkçe Detail mesajı gösterilir; yoksa yedek metin.
    public static string ProblemText(ApiResult result, string fallback)
        => result.Problem?.Detail ?? result.Problem?.Title ?? fallback;
}

// Randevu/ödeme/reçete listelerinde ham Id yerine ad gösterebilmek için doktor ve branş adları.
// Servis kapalı/yetkisiz ise boş kalır, ekran Id'yi gösterir (ad çözme ekranı bozmaz).
public sealed class AdminLookups
{
    public Dictionary<string, AdminDoctorListItemApiModel> Doctors { get; } = [];
    public Dictionary<string, string> Branches { get; } = [];

    public string Doctor(string id)
        => Doctors.TryGetValue(id, out var d) ? AdminDisplay.FullName(d) : id;

    public string Branch(string id)
        => Branches.TryGetValue(id, out var name) ? name : id;

    public static async Task<AdminLookups> LoadAsync(IAdminDoctorClient doctors, IAdminBranchClient branches, CancellationToken ct)
    {
        var lookups = new AdminLookups();
        var doctorResult = await doctors.GetAllAsync(ApiFailures.All, ct);
        if (doctorResult.TryGetValue(out var doctorList))
            foreach (var d in doctorList) lookups.Doctors[d.Id] = d;

        var branchResult = await branches.GetAllAsync(ApiFailures.All, ct);
        if (branchResult.TryGetValue(out var branchList))
            foreach (var b in branchList) lookups.Branches[b.Id] = b.Name;

        return lookups;
    }
}

// Sayfalama bileşeni (_Pager) için: hangi action'a, hangi süzgeçlerle gidileceği.
public sealed class PagerViewModel
{
    public string Action { get; init; } = "Index";
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
    public Dictionary<string, string?> Filters { get; init; } = [];

    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public Dictionary<string, string> RouteFor(int page)
    {
        var route = Filters.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToDictionary(f => f.Key, f => f.Value!);
        route["page"] = page.ToString(CultureInfo.InvariantCulture);
        return route;
    }
}
