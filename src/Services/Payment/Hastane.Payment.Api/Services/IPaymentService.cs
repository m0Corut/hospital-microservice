using Hastane.Payment.Api.Dtos;

namespace Hastane.Payment.Api.Services;

public interface IPaymentService
{
    Task<OperationResult<PaymentResultDto>> CreateAsync(string? idempotencyKey, CreatePaymentDto dto, CancellationToken ct);
    Task<OperationResult<PagedResult<PaymentResultDto>>> GetAllAsync(PaymentListQuery query, CancellationToken ct);
    Task<PaymentResultDto?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<OperationResult<PaymentResultDto>> RefundAsync(Guid id, CancellationToken ct);
}
