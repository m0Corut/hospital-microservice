namespace Hastane.Pharmacy.Api.Common;

public enum OperationStatus
{
    Ok,
    Created,
    NotFound,
    Conflict,
    Forbidden,
    ServiceUnavailable,
}

// Handler HTTP bilmez; sonucu bu zarfla bildirir, hangi koda çevrileceğine controller karar verir.
// Geçersiz GİRDİ burada değil: ValidationBehavior handler'a hiç ulaşmadan RequestValidationException fırlatır.
// Details: 409'da kalem listesi gibi ek bilgi (ProblemDetails "items" alanına yazılır).
public record OperationResult<T>(OperationStatus Status, T? Value = default, string? Error = null, object? Details = null)
{
    public bool IsSuccess => Status is OperationStatus.Ok or OperationStatus.Created;

    public static OperationResult<T> Ok(T value) => new(OperationStatus.Ok, value);
    public static OperationResult<T> Created(T value) => new(OperationStatus.Created, value);
    public static OperationResult<T> NotFound(string? error = null) => new(OperationStatus.NotFound, default, error);
    public static OperationResult<T> Conflict(string error, object? details = null) => new(OperationStatus.Conflict, default, error, details);
    public static OperationResult<T> Forbidden(string? error = null) => new(OperationStatus.Forbidden, default, error);
    public static OperationResult<T> ServiceUnavailable(string error) => new(OperationStatus.ServiceUnavailable, default, error);

    // Başka tipteki başarısız sonucu bu tipe taşır (değer yok, durum + mesaj + ayrıntı aynen kalır).
    public static OperationResult<T> Failure<TOther>(OperationResult<TOther> other) => new(other.Status, default, other.Error, other.Details);
}
