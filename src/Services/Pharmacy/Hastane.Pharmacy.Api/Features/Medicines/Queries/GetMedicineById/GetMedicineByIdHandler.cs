using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMedicineById;

public sealed class GetMedicineByIdHandler(PharmacyDbContext db)
    : IRequestHandler<GetMedicineByIdQuery, OperationResult<MedicineDto>>
{
    public async Task<OperationResult<MedicineDto>> Handle(GetMedicineByIdQuery query, CancellationToken ct)
    {
        var dto = await db.Medicines.AsNoTracking()
            .Where(m => m.Id == query.Id && m.IsActive)
            .Select(MedicineProjection.Projection)
            .FirstOrDefaultAsync(ct);

        return dto is null
            ? OperationResult<MedicineDto>.NotFound("İlaç bulunamadı.")
            : OperationResult<MedicineDto>.Ok(dto);
    }
}
