namespace Hastane.Pharmacy.Api.Integration;

public enum LookupOutcome
{
    Found,
    NotFound,    // 404: reçete yok
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

// Prescription servisinin yanıtından yalnız Pharmacy'nin ihtiyacı olan alanlar (ProjectReference yok, kendi küçük modelimiz).
public record PrescriptionInfo(Guid Id, string PatientId, string DoctorId, List<PrescriptionItemInfo> Items);

public record PrescriptionItemInfo(int LineNo, string MedicineName);
