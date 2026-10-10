using Hastane.Pharmacy.Api.Data;
using Hastane.Pharmacy.Api.Entities;
using Hastane.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Medicines;

public enum StockChangeOutcome
{
    Applied,
    NotFound,
    InsufficientStock,
}

public sealed record StockChange(StockChangeOutcome Outcome, StockMovement? Movement = null);

// Stok değiştirmenin TEK yolu (elle düzeltme de, reçete karşılama da buradan geçer).
//
// Neden "oku, kontrol et, yaz" değil: iki istek aynı anda stok=1 görür, ikisi de "yeter" der, stok -1 olur (kayıp güncelleme).
// Burada kontrol ve düşüm TEK SQL cümlesidir:
//   UPDATE Medicines SET Stock = Stock + @delta WHERE Id = @id AND Stock + @delta >= 0
// SQL Server satırı UPDATE sırasında kilitler; ikinci istek ilkinin sonucunu görür. 0 satır etkilenirse stok yetmiyordur.
//
// Çağıran kendi transaction'ını açar (karşılamada birden çok ilaç hep-ya-hiç) ve SaveChanges çağırır;
// hareket kaydı yalnız context'e eklenir.
public sealed class StockLedger(PharmacyDbContext db, TimeProvider clock, ICurrentUser user)
{
    public async Task<StockChange> ApplyAsync(
        Guid medicineId, int delta, StockMovementType type, string reason, Guid? prescriptionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var affected = await db.Medicines
            .Where(m => m.Id == medicineId && m.IsActive && m.Stock + delta >= 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Stock, m => m.Stock + delta)
                .SetProperty(m => m.UpdatedAt, now), ct);

        if (affected == 0)
        {
            var exists = await db.Medicines.AnyAsync(m => m.Id == medicineId && m.IsActive, ct);
            return new StockChange(exists ? StockChangeOutcome.InsufficientStock : StockChangeOutcome.NotFound);
        }

        // Güncellediğimiz satırın kilidini transaction bitene kadar tutuyoruz: bu okuma tam bizim yazdığımız değeri görür.
        var stockAfter = await db.Medicines.AsNoTracking()
            .Where(m => m.Id == medicineId)
            .Select(m => m.Stock)
            .SingleAsync(ct);

        var movement = new StockMovement
        {
            Id = Guid.CreateVersion7(),
            MedicineId = medicineId,
            Type = type,
            Delta = delta,
            StockAfter = stockAfter,
            Reason = reason,
            PrescriptionId = prescriptionId,
            CreatedBy = user.UserId,
            CreatedAt = now,
        };
        db.StockMovements.Add(movement);
        return new StockChange(StockChangeOutcome.Applied, movement);
    }
}
