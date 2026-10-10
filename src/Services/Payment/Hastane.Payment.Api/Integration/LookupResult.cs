namespace Hastane.Payment.Api.Integration;

public enum LookupOutcome
{
    Found,
    NotFound,    // 404: yok ya da bu kullanıcı göremiyor (sahiplik kararı karşı serviste)
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

// Appointment servisinin yanıtından yalnız Payment'ın ihtiyacı olan alanlar (ProjectReference yok, kendi küçük modelimiz).
public record AppointmentInfo(Guid Id, string DoctorId, string PatientId, string Status);

// Doktorun sunucu tarafında hesaplanan ücreti. Ücreti olmayan doktor için Amount null.
public record DoctorFee(decimal? Amount, string? Currency);
