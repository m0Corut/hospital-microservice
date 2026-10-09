namespace Hastane.Appointment.Api.Services;

public enum OperationStatus
{
    Ok,
    Created,
    NotFound,
    Conflict,
    Forbidden,
}

// Servis HTTP bilmez; sonucu bu zarfla bildirir, hangi koda çevrileceğine controller karar verir.
public record OperationResult<T>(OperationStatus Status, T? Value = default, string? Error = null)
{
    public static OperationResult<T> Ok(T value) => new(OperationStatus.Ok, value);
    public static OperationResult<T> Created(T value) => new(OperationStatus.Created, value);
    public static OperationResult<T> NotFound() => new(OperationStatus.NotFound);
    public static OperationResult<T> Forbidden() => new(OperationStatus.Forbidden);
    public static OperationResult<T> Conflict(string error) => new(OperationStatus.Conflict, default, error);
}
