namespace Hastane.Chat.Api.Integration;

public enum LookupOutcome
{
    Found,
    NotFound,    // 404
    Forbidden,   // 403
    Unavailable, // erişilemedi, zaman aşımı, 5xx, beklenmeyen yanıt
}

public record LookupResult<T>(LookupOutcome Outcome, T? Value = default) where T : class
{
    public static LookupResult<T> Found(T value) => new(LookupOutcome.Found, value);
    public static LookupResult<T> NotFound() => new(LookupOutcome.NotFound);
    public static LookupResult<T> Forbidden() => new(LookupOutcome.Forbidden);
    public static LookupResult<T> Unavailable() => new(LookupOutcome.Unavailable);
}

// Karşı servis yanıtlarından yalnız Chat'in ihtiyacı olan alanlar (ProjectReference yok, kendi küçük modellerimiz).
public record AppointmentRef(Guid Id);

public record PublicDoctorInfo(string Id, string FirstName, string LastName, string? Title);

public record UserInfo(string Id, string FirstName, string LastName);
