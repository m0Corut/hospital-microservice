using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using Hastane.Pharmacy.Api.Entities;
using Hastane.Shared.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.CreateMedicine;

public sealed class CreateMedicineHandler(PharmacyDbContext db, ICurrentUser user, TimeProvider clock)
    : IRequestHandler<CreateMedicineCommand, OperationResult<MedicineDto>>
{
    public async Task<OperationResult<MedicineDto>> Handle(CreateMedicineCommand command, CancellationToken ct)
    {
        var barcode = command.Barcode.Trim();
        if (await db.Medicines.AnyAsync(m => m.Barcode == barcode, ct))
            return OperationResult<MedicineDto>.Conflict("Bu barkodla kayıtlı bir ilaç zaten var.");

        var now = clock.GetUtcNow().UtcDateTime;
        var medicine = new Medicine
        {
            Id = Guid.CreateVersion7(),
            Name = command.Name.Trim(),
            NormalizedName = TurkishText.Fold(command.Name),
            ActiveIngredient = command.ActiveIngredient.Trim(),
            NormalizedIngredient = TurkishText.Fold(command.ActiveIngredient),
            Form = command.Form,
            Strength = command.Strength.Trim(),
            Barcode = barcode,
            Price = command.Price,
            Stock = command.Stock,
            MinStock = command.MinStock,
            RequiresPrescription = command.RequiresPrescription,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Medicines.Add(medicine);

        // Başlangıç stoğu da defterde görünsün: "stok neden 40?" sorusunun ilk cevabı.
        if (command.Stock > 0)
        {
            db.StockMovements.Add(new StockMovement
            {
                Id = Guid.CreateVersion7(),
                MedicineId = medicine.Id,
                Type = StockMovementType.Initial,
                Delta = command.Stock,
                StockAfter = command.Stock,
                Reason = "İlk stok kaydı",
                CreatedBy = user.UserId,
                CreatedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            // Yukarıdaki AnyAsync ile Save arasında başka istek aynı barkodu yazdıysa: asıl garanti veritabanındaki tekil dizindir.
            return OperationResult<MedicineDto>.Conflict("Bu barkodla kayıtlı bir ilaç zaten var.");
        }

        return OperationResult<MedicineDto>.Created(MedicineProjection.ToDto(medicine));
    }
}
