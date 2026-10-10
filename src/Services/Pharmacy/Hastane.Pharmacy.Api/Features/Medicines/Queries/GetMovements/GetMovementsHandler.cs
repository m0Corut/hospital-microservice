using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMovements;

public sealed class GetMovementsHandler(PharmacyDbContext db)
    : IRequestHandler<GetMovementsQuery, OperationResult<PagedResult<StockMovementDto>>>
{
    public async Task<OperationResult<PagedResult<StockMovementDto>>> Handle(GetMovementsQuery query, CancellationToken ct)
    {
        // Silinmiş (pasif) ilacın geçmişi de okunabilir: defter silinmez.
        if (!await db.Medicines.AnyAsync(m => m.Id == query.MedicineId, ct))
            return OperationResult<PagedResult<StockMovementDto>>.NotFound("İlaç bulunamadı.");

        var movements = db.StockMovements.AsNoTracking().Where(s => s.MedicineId == query.MedicineId);
        var total = await movements.CountAsync(ct);
        var items = await movements
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(MedicineProjection.MovementProjection)
            .ToListAsync(ct);

        return OperationResult<PagedResult<StockMovementDto>>.Ok(new PagedResult<StockMovementDto>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        });
    }
}
