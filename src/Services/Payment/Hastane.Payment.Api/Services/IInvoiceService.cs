using Hastane.Payment.Api.Dtos;

namespace Hastane.Payment.Api.Services;

public interface IInvoiceService
{
    Task<OperationResult<PagedResult<ResultInvoiceDto>>> GetAllAsync(InvoiceListQuery query, CancellationToken ct);
    Task<GetByIdInvoiceDto?> GetByIdAsync(Guid id, CancellationToken ct);
}
