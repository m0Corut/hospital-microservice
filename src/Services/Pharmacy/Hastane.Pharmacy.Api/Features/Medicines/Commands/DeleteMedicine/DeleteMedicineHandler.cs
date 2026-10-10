using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.DeleteMedicine;

// Yumuşak silme: satır kalır (hareket defteri ve karşılama kayıtları ona bağlı), IsActive=false olur.
// Pasif ilaç listelerde, aramada ve karşılama eşleştirmesinde görünmez. Not: barkodu da dolu kalır (tekil dizin pasifleri de kapsar).
public sealed class DeleteMedicineHandler(PharmacyDbContext db, TimeProvider clock)
    : IRequestHandler<DeleteMedicineCommand, OperationResult<Unit>>
{
    public async Task<OperationResult<Unit>> Handle(DeleteMedicineCommand command, CancellationToken ct)
    {
        var medicine = await db.Medicines.FirstOrDefaultAsync(m => m.Id == command.Id && m.IsActive, ct);
        if (medicine is null) return OperationResult<Unit>.NotFound("İlaç bulunamadı.");

        medicine.IsActive = false;
        medicine.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return OperationResult<Unit>.Ok(Unit.Value);
    }
}
