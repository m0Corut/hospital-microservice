using Hastane.Order.Api.Dtos;
using Hastane.Order.Api.Entities;

namespace Hastane.Order.Api.Services;

public interface IOrderService
{
    Task<OperationResult<GetByIdOrderDto>> CheckoutAsync(
        string? idempotencyKey, CheckoutDto dto, string patientName, CancellationToken ct);

    Task<PagedResult<ResultOrderDto>> GetAllAsync(OrderListQuery query, CancellationToken ct);

    // Sahip ya da Admin dışındaki kullanıcı için null (controller 404 verir: başkasının siparişinin varlığı sızmaz).
    Task<GetByIdOrderDto?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<OperationResult<GetByIdOrderDto>> UpdateStatusAsync(Guid id, OrderStatus target, CancellationToken ct);

    Task<OperationResult<GetByIdOrderDto>> CancelAsync(Guid id, string? reason, CancellationToken ct);
}
