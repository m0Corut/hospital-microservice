using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Integration;

namespace Hastane.Appointment.Api.Services;

// Saf hesap (veritabanı yok, ağ yok): "bu doktorun programı bu yerel günde hangi slotlara bölünür?".
// Hem boş saat listesi hem POST'taki "bu saat programda mı?" kontrolü bunu kullanır; iki yer aynı hesabı paylaşır,
// biri "boş" dediği saati diğeri reddedemez.
public static class SlotCalculator
{
    public static List<AvailableSlotDto> SlotsForLocalDate(DoctorSchedule schedule, DateOnly localDate, TimeZoneInfo zone)
    {
        var slots = new List<AvailableSlotDto>();
        var seenStarts = new HashSet<DateTime>();

        foreach (var location in schedule.ClinicLocations)
        {
            foreach (var entry in location.Schedule ?? [])
            {
                if (entry.DayOfWeek != localDate.DayOfWeek || entry.SlotMinutes <= 0) continue;
                if (!TryParseTime(entry.StartTime, out var start) || !TryParseTime(entry.EndTime, out var end)) continue;

                var step = TimeSpan.FromMinutes(entry.SlotMinutes);
                // Slot, bitişi aralığa SIĞIYORSA üretilir: 09:00-10:00, 30 dk => 09:00 ve 09:30; 10:00 değil.
                for (var t = start; t + step <= end; t += step)
                {
                    var localStart = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).Add(t);
                    if (zone.IsInvalidTime(localStart) || zone.IsAmbiguousTime(localStart)) continue; // yaz saati kaydırması
                    var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, zone);
                    // Aynı başlangıçta iki klinik varsa ilki kazanır: doktor aynı anda iki yerde olamaz.
                    if (!seenStarts.Add(startUtc)) continue;
                    slots.Add(new AvailableSlotDto
                    {
                        Start = startUtc,
                        End = startUtc.AddMinutes(entry.SlotMinutes),
                        ClinicName = location.ClinicName,
                        City = location.City,
                    });
                }
            }
        }

        return slots.OrderBy(s => s.Start).ToList();
    }

    // "HH:mm"; bitiş için "24:00" de kabul (gün sonu).
    private static bool TryParseTime(string? text, out TimeSpan time)
    {
        time = default;
        if (text is null || text.Length != 5 || text[2] != ':') return false;
        if (!int.TryParse(text.AsSpan(0, 2), out var h) || !int.TryParse(text.AsSpan(3, 2), out var m)) return false;
        if (h is < 0 or > 24 || m is < 0 or > 59 || (h == 24 && m != 0)) return false;
        time = new TimeSpan(h, m, 0);
        return true;
    }
}
