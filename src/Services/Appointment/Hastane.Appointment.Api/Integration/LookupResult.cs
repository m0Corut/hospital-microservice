namespace Hastane.Appointment.Api.Integration;

public enum LookupOutcome
{
    Found,
    NotFound,    // 404: böyle bir doktor yok
    Unavailable, // erişilemedi, zaman aşımı, 5xx, 401/403, bozuk yanıt
}

public record LookupResult<T>(LookupOutcome Outcome, T? Value = default) where T : class
{
    public static LookupResult<T> Found(T value) => new(LookupOutcome.Found, value);
    public static LookupResult<T> NotFound() => new(LookupOutcome.NotFound);
    public static LookupResult<T> Unavailable() => new(LookupOutcome.Unavailable);
}
