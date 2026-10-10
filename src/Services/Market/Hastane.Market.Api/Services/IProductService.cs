using Hastane.Market.Api.Dtos;

namespace Hastane.Market.Api.Services;

public interface IProductService
{
    // includeInactive yalnız Admin için etkilidir (controller isAdmin'i token'dan verir).
    Task<OperationResult<PagedResult<ResultProductDto>>> GetAllAsync(ProductListQuery query, bool isAdmin, CancellationToken ct);

    Task<ResultProductDto?> GetByIdAsync(Guid id, bool isAdmin, CancellationToken ct);

    Task<OperationResult<ResultProductDto>> CreateAsync(CreateProductDto dto, CancellationToken ct);

    Task<OperationResult<ResultProductDto>> UpdateAsync(Guid id, UpdateProductDto dto, CancellationToken ct);

    Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken ct);
}
