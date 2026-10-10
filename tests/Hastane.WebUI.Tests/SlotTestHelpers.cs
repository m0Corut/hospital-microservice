using System.Globalization;

namespace Hastane.WebUI.Tests;

// Saat seçici testlerinin ortak yardımcıları. Randevu saatleri Türkiye saatiyle (UTC+3, yaz saati yok) gösterilir:
// testler sunucunun saat dilimine bağlı olmasın diye saat farkı sabit +3 alınır.
internal static class SlotTestHelpers
{
    private static DateTime IstanbulNow => DateTime.UtcNow.AddHours(3);

    /// <summary>Bugünden <paramref name="daysAhead"/> gün sonrasının İstanbul saatiyle hour:minute anının UTC karşılığı.</summary>
    public static DateTime SlotUtc(int daysAhead = 2, int hour = 10, int minute = 0)
        => DateTime.SpecifyKind(IstanbulNow.Date.AddDays(daysAhead).AddHours(hour - 3).AddMinutes(minute), DateTimeKind.Utc);

    /// <summary>Formda radio'nun taşıdığı değer: slotun UTC başlangıcı.</summary>
    public static string SlotValue(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>İstanbul takvimiyle bugünden <paramref name="daysAhead"/> gün sonrası (yyyy-MM-dd).</summary>
    public static string DayText(int daysAhead) => IstanbulNow.Date.AddDays(daysAhead).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>GET doctors/{id}/public gövdesi: Doctor() ile aynı, ama telefon ve e-posta YOK.</summary>
    public static object PublicDoctor() => new
    {
        id = "d1",
        firstName = "Ali",
        lastName = "Veli",
        title = "Dr.",
        gender = "Male",
        degrees = "Kardiyoloji Uzmanı",
        branchId = "b0",
        biography = "Kalp hastalıkları uzmanı.",
        services = new[] { "EKG" },
        educations = new[] { new { school = "Ankara Üniversitesi", department = "Tıp", startYear = 2000, endYear = 2006 } },
        clinicLocations = new[]
        {
            new
            {
                clinicName = "Merkez Klinik", city = "Ankara", district = "Çankaya", address = "Atatürk Bulvarı 1", floor = "3",
                hourlyFee = 500m, currency = "TRY",
                schedule = new[] { new { dayOfWeek = "Monday", startTime = "09:00", endTime = "12:00", slotMinutes = 30 } },
            },
        },
        primaryCity = "Ankara",
        minFee = 500m,
        maxFee = 500m,
        currency = "TRY",
    };

    public static object Slot(DateTime utc) => new { start = utc, end = utc.AddMinutes(30), clinicName = "Merkez Klinik", city = "Ankara" };

    /// <summary>GET appointments/availability yanıtını ayarlar; sorgu dizeleri CallLog'a yazılır (FakeGateway yalnız yolu kaydeder).</summary>
    public static CallLog MapAvailability(this FakeGateway gateway, params DateTime[] starts)
        => gateway.MapLogged("GET", "/gateway/appointments/availability", 200, starts.Select(Slot).ToArray());
}
