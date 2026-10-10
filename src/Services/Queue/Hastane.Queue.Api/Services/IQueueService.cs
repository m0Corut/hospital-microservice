using Hastane.Queue.Api.Dtos;

namespace Hastane.Queue.Api.Services;

public interface IQueueService
{
    // Yeni kayıt: Created; aynı randevu ikinci kez: Ok (mevcut kayıt).
    Task<OperationResult<MyQueueEntryDto>> CheckInAsync(Guid appointmentId, string? fullName, CancellationToken ct);

    Task<List<MyQueueEntryDto>> GetMineTodayAsync(CancellationToken ct);

    Task<OperationResult<List<QueueEntryDto>>> GetDoctorTodayAsync(string? doctorId, CancellationToken ct);

    Task<OperationResult<QueueEntryDto>> CallNextAsync(CancellationToken ct);

    Task<OperationResult<QueueEntryDto>> CallAsync(Guid id, CancellationToken ct);

    Task<OperationResult<QueueEntryDto>> CompleteAsync(Guid id, CancellationToken ct);

    Task<OperationResult<QueueEntryDto>> NoShowAsync(Guid id, CancellationToken ct);

    Task<QueueDisplayDto> GetDisplayAsync(string doctorId, CancellationToken ct);
}
