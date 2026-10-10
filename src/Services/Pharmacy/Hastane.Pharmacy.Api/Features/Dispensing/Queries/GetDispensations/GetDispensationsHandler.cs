using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Queries.GetDispensations;

public sealed class GetDispensationsHandler(PharmacyDbContext db)
    : IRequestHandler<GetDispensationsQuery, OperationResult<PagedResult<DispensationDto>>>
{
    public async Task<OperationResult<PagedResult<DispensationDto>>> Handle(GetDispensationsQuery query, CancellationToken ct)
    {
        var total = await db.Dispensations.CountAsync(ct);
        var items = await db.Dispensations.AsNoTracking()
            .OrderByDescending(d => d.DispensedAt).ThenByDescending(d => d.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(DispensationProjection.Projection)
            .ToListAsync(ct);

        return OperationResult<PagedResult<DispensationDto>>.Ok(new PagedResult<DispensationDto>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        });
    }
}
