using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.UpdateMedicine;

public sealed class UpdateMedicineHandler(PharmacyDbContext db, TimeProvider clock)
    : IRequestHandler<UpdateMedicineCommand, OperationResult<MedicineDto>>
{
    public async Task<OperationResult<MedicineDto>> Handle(UpdateMedicineCommand command, CancellationToken ct)
    {
        // Yazma tarafı entity üzerinden: yükle, alanları değiştir, kaydet (EF değişiklik izleme + RowVersion kontrolü).
        var medicine = await db.Medicines.FirstOrDefaultAsync(m => m.Id == command.Id && m.IsActive, ct);
        if (medicine is null) return OperationResult<MedicineDto>.NotFound("İlaç bulunamadı.");

        var barcode = command.Barcode.Trim();
        if (barcode != medicine.Barcode && await db.Medicines.AnyAsync(m => m.Barcode == barcode && m.Id != medicine.Id, ct))
            return OperationResult<MedicineDto>.Conflict("Bu barkodla kayıtlı başka bir ilaç var.");

        // İstemcinin gördüğü sürümü "orijinal değer" yap: UPDATE ... WHERE RowVersion = @istemcininki.
        if (command.RowVersion is { Length: > 0 })
            db.Entry(medicine).Property(m => m.RowVersion).OriginalValue = command.RowVersion;

        medicine.Name = command.Name.Trim();
        medicine.NormalizedName = TurkishText.Fold(command.Name);
        medicine.ActiveIngredient = command.ActiveIngredient.Trim();
        medicine.NormalizedIngredient = TurkishText.Fold(command.ActiveIngredient);
        medicine.Form = command.Form;
        medicine.Strength = command.Strength.Trim();
        medicine.Barcode = barcode;
        medicine.Price = command.Price;
        medicine.MinStock = command.MinStock;
        medicine.RequiresPrescription = command.RequiresPrescription;
        medicine.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult<MedicineDto>.Conflict("İlaç kaydı siz düzenlerken başka biri tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.");
        }
        catch (DbUpdateException ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            return OperationResult<MedicineDto>.Conflict("Bu barkodla kayıtlı başka bir ilaç var.");
        }

        return OperationResult<MedicineDto>.Ok(MedicineProjection.ToDto(medicine));
    }
}
