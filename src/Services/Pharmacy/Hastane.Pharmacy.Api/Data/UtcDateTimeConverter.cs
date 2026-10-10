using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hastane.Pharmacy.Api.Data;

// SQL Server datetime2 saat dilimi saklamaz; okurken Kind "Unspecified" olur ve JSON'a "Z" olmadan çıkar.
// Yazarken UTC'ye çeviriyor, okurken UTC işaretliyoruz (diğer servislerle aynı desen).
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
