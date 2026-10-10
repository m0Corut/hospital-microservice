namespace Hastane.Patient.Api.Integration;

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

// Appointment listesinden yalnız "var mı" sorusu için gereken alan (ProjectReference yok, kendi küçük modelimiz).
public record AppointmentRef(Guid Id);

// Doctor herkese açık profilinden yalnız varlık kanıtı için kimlik.
public record DoctorRef(string? Id);
