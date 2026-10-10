using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using Hastane.Pharmacy.Api.Entities;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.AdjustStock;

public sealed class AdjustStockHandler(PharmacyDbContext db, StockLedger ledger)
    : IRequestHandler<AdjustStockCommand, OperationResult<StockMovementDto>>
{
    public async Task<OperationResult<StockMovementDto>> Handle(AdjustStockCommand command, CancellationToken ct)
    {
        // Transaction: stok güncellemesi + hareket kaydı birlikte olur ya da hiç olmaz.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var change = await ledger.ApplyAsync(
            command.MedicineId, command.Delta, StockMovementType.Adjustment, command.Reason.Trim(), prescriptionId: null, ct);

        switch (change.Outcome)
        {
            case StockChangeOutcome.NotFound:
                return OperationResult<StockMovementDto>.NotFound("İlaç bulunamadı.");
            case StockChangeOutcome.InsufficientStock:
                return OperationResult<StockMovementDto>.Conflict("Stok eksiye düşemez: mevcut stok bu çıkış için yetersiz.");
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return OperationResult<StockMovementDto>.Ok(MedicineProjection.ToDto(change.Movement!));
    }
}
