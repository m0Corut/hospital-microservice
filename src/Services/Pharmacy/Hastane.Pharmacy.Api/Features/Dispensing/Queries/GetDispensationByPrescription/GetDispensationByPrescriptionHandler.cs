using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Queries.GetDispensationByPrescription;

public sealed class GetDispensationByPrescriptionHandler(PharmacyDbContext db)
    : IRequestHandler<GetDispensationByPrescriptionQuery, OperationResult<DispensationDto>>
{
    public async Task<OperationResult<DispensationDto>> Handle(GetDispensationByPrescriptionQuery query, CancellationToken ct)
    {
        var dto = await db.Dispensations.AsNoTracking()
            .Where(d => d.PrescriptionId == query.PrescriptionId)
            .Select(DispensationProjection.Projection)
            .FirstOrDefaultAsync(ct);

        return dto is null
            ? OperationResult<DispensationDto>.NotFound("Bu reçete için karşılama kaydı yok.")
            : OperationResult<DispensationDto>.Ok(dto);
    }
}
