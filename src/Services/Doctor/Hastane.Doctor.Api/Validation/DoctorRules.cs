using System.Text.RegularExpressions;
using Hastane.Doctor.Api.Dtos;

namespace Hastane.Doctor.Api.Validation;

// Tek kaynak: sınırlar ve kurallar. Hem DTO katmanı (öznitelik / IValidatableObject)
// hem servis katmanı (DoctorInputValidator) buradan okur; iki katman aynı kuralı farklı yazıp ayrışmasın.
public static partial class DoctorRules
{
    public const int MaxDegrees = 300;
    public const int MaxServices = 20;
    public const int MaxServiceLength = 80;
    public const int MaxGallery = 10;
    public const int MaxUrlLength = 500;
    public const int MaxSocialLength = 300;
    public const int MaxScheduleSlots = 21;
    public const int MinSlotMinutes = 5;
    public const int MaxSlotMinutes = 120;

    public const string TimePattern = @"^([01]\d|2[0-3]):[0-5]\d$";
    public const string CurrencyPattern = "^[A-Z]{3}$";

    [GeneratedRegex(TimePattern)]
    private static partial Regex TimeRegex();

    public static bool IsTime(string? s) => s is not null && TimeRegex().IsMatch(s);

    public static bool IsHttpUrl(string? s, int maxLength)
        => !string.IsNullOrWhiteSpace(s) && s.Length <= maxLength
           && Uri.TryCreate(s, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Bir konumun haftalık programını doğrular; hata iletilerini döndürür (boş = geçerli).
    /// Çakışma: aynı gün içinde [Start, End) aralıkları kesişmemeli (bitiş = sonraki başlangıç serbest).
    /// </summary>
    public static IEnumerable<string> ValidateSchedule(IReadOnlyList<ScheduleSlotDto?>? schedule)
    {
        if (schedule is null) yield break;
        if (schedule.Count > MaxScheduleSlots)
            yield return $"Schedule en çok {MaxScheduleSlots} aralık içerebilir.";

        var valid = new List<ScheduleSlotDto>();
        foreach (var slot in schedule)
        {
            if (slot is null) { yield return "Schedule öğesi boş olamaz."; continue; }
            var ok = true;
            if (!Enum.IsDefined(slot.DayOfWeek)) { yield return "Schedule.DayOfWeek geçersiz."; ok = false; }
            if (!IsTime(slot.StartTime)) { yield return "Schedule.StartTime 'HH:mm' biçiminde olmalı."; ok = false; }
            if (!IsTime(slot.EndTime)) { yield return "Schedule.EndTime 'HH:mm' biçiminde olmalı."; ok = false; }
            if (slot.SlotMinutes < MinSlotMinutes || slot.SlotMinutes > MaxSlotMinutes)
            { yield return $"Schedule.SlotMinutes {MinSlotMinutes}..{MaxSlotMinutes} arasında olmalı."; ok = false; }
            if (ok && string.CompareOrdinal(slot.EndTime, slot.StartTime) <= 0)
            { yield return "Schedule.EndTime, StartTime'dan sonra olmalı."; ok = false; }
            if (ok) valid.Add(slot);
        }

        // Çakışma: günlere böl, başlangıca göre sırala, her aralığın başı öncekinin sonundan önce olmamalı.
        foreach (var day in valid.GroupBy(s => s.DayOfWeek))
        {
            var sorted = day.OrderBy(s => s.StartTime, StringComparer.Ordinal).ToList();
            for (var i = 1; i < sorted.Count; i++)
                if (string.CompareOrdinal(sorted[i].StartTime, sorted[i - 1].EndTime) < 0)
                    yield return $"Schedule çakışıyor: {day.Key} {sorted[i - 1].StartTime}-{sorted[i - 1].EndTime} ile {sorted[i].StartTime}-{sorted[i].EndTime}.";
        }
    }
}
