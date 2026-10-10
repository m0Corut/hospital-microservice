using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hastane.Market.Api.Data;

// SQL Server datetime2 saat dilimi saklamaz; okurken Kind'i UTC işaretliyoruz ki JSON'a "Z" ile çıksın.
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
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
