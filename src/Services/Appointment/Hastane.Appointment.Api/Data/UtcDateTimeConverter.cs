using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hastane.Appointment.Api.Data;

// SQL Server datetime2 saat dilimi saklamaz; EF okurken Kind'i "Unspecified" yapar ve JSON'a "Z" olmadan çıkar.
// Yazarken UTC'ye çeviriyor, okurken UTC işaretliyoruz.
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(v => Normalize(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }

    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc), // saat dilimi belirtilmemişse UTC varsay
    };

    // Randevu slotu dakikadır: saniye ve altı atılır, böylece 10:00:00 ile 10:00:30 aynı slot sayılır.
    public static DateTime NormalizeToMinute(DateTime value)
    {
        var utc = Normalize(value);
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    }
}
