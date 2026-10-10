namespace Hastane.Patient.Api.Services;

public enum OperationStatus
{
    Ok,
    NoContent,
    NotFound,
    Conflict,
    Validation,
    ServiceUnavailable,
}

// Servis HTTP bilmez; sonucu bu zarfla bildirir, hangi koda çevrileceğine controller karar verir.
public record OperationResult<T>(OperationStatus Status, T? Value = default, string? Error = null)
{
    public static OperationResult<T> Ok(T value) => new(OperationStatus.Ok, value);
    public static OperationResult<T> NoContent() => new(OperationStatus.NoContent);
    public static OperationResult<T> NotFound(string? error = null) => new(OperationStatus.NotFound, default, error);
    public static OperationResult<T> Conflict(string error) => new(OperationStatus.Conflict, default, error);
    public static OperationResult<T> Validation(string error) => new(OperationStatus.Validation, default, error);
    public static OperationResult<T> ServiceUnavailable(string error) => new(OperationStatus.ServiceUnavailable, default, error);
}
