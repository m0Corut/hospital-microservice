using System.Globalization;
using Hastane.WebUI.ViewModels.Shared;

namespace Hastane.WebUI.ViewModels.Public;

// Ekran metni yardımcıları (Türkçe etiketler, tarih ve para biçimi). API değerleri İngilizce/UTC; burada çevrilir.
public static class DisplayText
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string DoctorName(string? title, string firstName, string lastName)
        => string.Join(' ', new[] { title, firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static string Gender(string? gender) => gender switch
    {
        "Male" => "Erkek",
        "Female" => "Kadın",
        "Other" => "Diğer",
        _ => string.Empty,
    };

    public static string AppointmentStatus(string status) => status switch
    {
        "Pending" => "Onay bekliyor",
        "Confirmed" => "Onaylandı",
        "Cancelled" => "İptal edildi",
        "Completed" => "Tamamlandı",
        _ => status,
    };

    public static string AppointmentBadge(string status) => status switch
    {
        "Pending" => "text-bg-warning",
        "Confirmed" => "text-bg-primary",
        "Completed" => "text-bg-success",
        "Cancelled" => "text-bg-secondary",
        _ => "text-bg-light",
    };

    public static string PaymentStatus(string status) => status switch
    {
        "Pending" => "İşleniyor",
        "Paid" => "Ödendi",
        "Failed" => "Başarısız",
        "Refunded" => "İade edildi",
        _ => status,
    };

    public static string PaymentBadge(string status) => status switch
    {
        "Paid" => "text-bg-success",
        "Pending" => "text-bg-warning",
        "Failed" => "text-bg-danger",
        "Refunded" => "text-bg-secondary",
        _ => "text-bg-light",
    };

    public static string Day(string dayOfWeek) => dayOfWeek switch
    {
        "Monday" => "Pazartesi",
        "Tuesday" => "Salı",
        "Wednesday" => "Çarşamba",
        "Thursday" => "Perşembe",
        "Friday" => "Cuma",
        "Saturday" => "Cumartesi",
        "Sunday" => "Pazar",
        _ => dayOfWeek,
    };

    // API UTC verir; ekranda yerel saat, dd.MM.yyyy HH:mm.
    public static string DateTimeLocal(DateTime utc)
        => AppointmentTime.ToClinicLocal(utc).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string DateLocal(DateTime utc)
        => AppointmentTime.ToClinicLocal(utc).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static string Money(decimal amount, string? currency)
        => amount.ToString("N2", Tr) + " " + (currency ?? string.Empty);

    public static string FeeRange(decimal? min, decimal? max, string? currency)
    {
        if (min is null && max is null) return "Ücret belirtilmemiş";
        if (min is null || max is null || min == max) return Money(min ?? max!.Value, currency);
        return Money(min.Value, currency) + " - " + Money(max.Value, currency);
    }

    public static DateTime EnsureUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
}
