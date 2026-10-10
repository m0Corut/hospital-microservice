using System.Globalization;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Public;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Hastane.WebUI.ViewModels.Shared;

// Gün düğmeleri + o günün boş saat ızgarası (randevu alma ve yeniden planlama ortak). Sunucu tarafı çizilir: gün düğmesi bir GET bağlantısıdır.
public sealed class SlotPickerViewModel
{
    public const int DayCount = 14;

    public List<SlotDayOption> Days { get; init; } = [];
    public DateOnly SelectedDate { get; init; }
    public List<SlotOption> Slots { get; init; } = [];

    // Formdan gelen seçili slot (hatayla dönülürse işaretli kalır).
    public string? SelectedStart { get; set; }

    // Availability uç noktası 503 verdi (doktor servisi kapalı): saat gösterilemez.
    public bool Unavailable { get; init; }

    public string SelectedDateText => AppointmentTime.DateText(SelectedDate);
    public SlotDayOption? NextDay => Days.FirstOrDefault(d => d.Date > SelectedDate);
}

public sealed record SlotDayOption(DateOnly Date, string Label, string Url, bool Selected);

// Value: slotun UTC başlangıcı (POST'ta API'ye bu gider). Text: İstanbul saati HH:mm.
public sealed record SlotOption(string Value, string Text, string? ClinicName);

public static class AppointmentTime
{
    // Kliniklerin yerel saati: randevu saatleri İstanbul saatiyle gösterilir (sunucunun saat dilimine bağlı değil).
    public static readonly TimeZoneInfo Zone = ResolveZone();

    private static readonly string[] ShortDays = ["Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt"];

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
    }

    // TÜM ekran saatleri bu dönüşümle gösterilir (sunucunun saat dilimine bağlı değil). Kind'siz değer UTC kabul edilir.
    public static DateTime ToClinicLocal(DateTime value)
        => TimeZoneInfo.ConvertTimeFromUtc(DisplayText.EnsureUtc(value), Zone);

    // İstanbul takvim gününün başlangıcının UTC karşılığı (liste süzgeçleri, takvim haftası, "bugün").
    public static DateTime ClinicLocalToUtc(DateTime clinicLocal)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(clinicLocal, DateTimeKind.Unspecified), Zone);

    // Şu an İstanbul duvar saatiyle.
    public static DateTime NowLocal() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    public static DateOnly Today() => DateOnly.FromDateTime(NowLocal());

    public static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string SlotValue(DateTime utc)
        => DisplayText.EnsureUtc(utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string SlotText(DateTime utc)
        => ToClinicLocal(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    // ?date= değeri: bozuksa ya da önümüzdeki 14 günün dışındaysa bugün.
    public static DateOnly ParseDay(string? text)
    {
        var today = Today();
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return today;
        return day < today || day >= today.AddDays(SlotPickerViewModel.DayCount) ? today : day;
    }

    // Seçilen slot (UTC metni) geçerli ve gelecekte olmalı. Boş değer [Required] hatasıyla zaten bildirilir.
    public static DateTime? ParseSlot(string? text, ModelStateDictionary state, string key)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
        {
            state.AddModelError(key, "Geçersiz saat seçimi.");
            return null;
        }

        if (utc > DateTime.UtcNow) return utc;
        state.AddModelError(key, "Randevu tarihi gelecekte olmalı.");
        return null;
    }

    // Yazma uçlarının sonucu forma yazılır: 503 (doktor/randevu servisi kapalı) sayfa yerine form mesajı olur.
    public static void AddBookingProblem(this ModelStateDictionary state, ApiResult result, string fallback)
    {
        if (result.Kind == ApiResultKind.Unavailable)
            state.AddModelError(string.Empty, "Doktorun programına şu an ulaşılamıyor. Lütfen biraz sonra tekrar deneyin.");
        else
            state.AddApiProblem(result, fallback);
    }

    public static async Task<SlotPickerViewModel> BuildPickerAsync(
        IAppointmentAvailabilityClient availability, string doctorId, string? dateText, Func<DateOnly, string> dayUrl, CancellationToken ct)
    {
        var selected = ParseDay(dateText);
        var today = Today();
        var days = Enumerable.Range(0, SlotPickerViewModel.DayCount)
            .Select(i => today.AddDays(i))
            .Select(d => new SlotDayOption(d, $"{ShortDays[(int)d.DayOfWeek]} {d.ToString("dd.MM", CultureInfo.InvariantCulture)}", dayUrl(d), d == selected))
            .ToList();

        var result = await availability.GetAsync(doctorId, selected, ApiFailures.Unavailable, ct);
        var slots = (result.Value ?? [])
            .OrderBy(s => s.Start)
            .Select(s => new SlotOption(SlotValue(s.Start), SlotText(s.Start), string.IsNullOrWhiteSpace(s.ClinicName) ? null : s.ClinicName))
            .ToList();

        return new SlotPickerViewModel
        {
            Days = days,
            SelectedDate = selected,
            Slots = slots,
            Unavailable = !result.IsSuccess,
        };
    }
}
