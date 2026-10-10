namespace Hastane.Market.Api.Services;

public enum OperationStatus
{
    Ok,
    Created,
    NotFound,
    Conflict,
    Validation,
    ServiceUnavailable,
}

// Servis HTTP bilmez; sonucu bu zarfla bildirir, hangi koda çevrileceğine controller karar verir.
public record OperationResult<T>(OperationStatus Status, T? Value = default, string? Error = null)
{
    public static OperationResult<T> Ok(T value) => new(OperationStatus.Ok, value);
    public static OperationResult<T> Created(T value) => new(OperationStatus.Created, value);
    public static OperationResult<T> NotFound(string? error = null) => new(OperationStatus.NotFound, default, error);
    public static OperationResult<T> Validation(string error) => new(OperationStatus.Validation, default, error);
    public static OperationResult<T> Conflict(string error) => new(OperationStatus.Conflict, default, error);
    public static OperationResult<T> ServiceUnavailable(string error) => new(OperationStatus.ServiceUnavailable, default, error);
}
