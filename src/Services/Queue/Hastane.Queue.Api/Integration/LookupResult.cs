namespace Hastane.Queue.Api.Integration;

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

// Appointment servisinin yanıtından yalnız Queue'nun ihtiyacı olan alanlar (ProjectReference yok, kendi küçük modelimiz).
// AppointmentDate UTC gelir; "bugün mü" kararı kliniğin saat diliminde verilir.
public record AppointmentInfo(Guid Id, string DoctorId, string PatientId, string Status, DateTime AppointmentDate);
