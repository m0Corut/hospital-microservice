using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetLowStock;

public sealed class GetLowStockHandler(PharmacyDbContext db)
    : IRequestHandler<GetLowStockQuery, OperationResult<PagedResult<MedicineDto>>>
{
    public async Task<OperationResult<PagedResult<MedicineDto>>> Handle(GetLowStockQuery query, CancellationToken ct)
    {
        var low = db.Medicines.AsNoTracking().Where(m => m.IsActive && m.Stock <= m.MinStock);

        var total = await low.CountAsync(ct);
        var items = await low
            .OrderBy(m => m.Stock - m.MinStock).ThenBy(m => m.NormalizedName)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(MedicineProjection.Projection)
            .ToListAsync(ct);

        return OperationResult<PagedResult<MedicineDto>>.Ok(new PagedResult<MedicineDto>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        });
    }
}
