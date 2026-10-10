using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using Hastane.Pharmacy.Api.Entities;
using Hastane.Pharmacy.Api.Features.Medicines;
using Hastane.Shared.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Commands.DispensePrescription;

// Reçete karşılama: HEPSİ YA HİÇ. Eşleşmeyen ya da stoğu yetmeyen tek kalem varsa hiçbir ilaç düşülmez (409 + kalem listesi).
// Hepsi uygunsa stok düşümleri + hareket kayıtları + Dispensation TEK transaction'da yazılır.
public sealed class DispensePrescriptionHandler(
    PharmacyDbContext db, DispensePlanner planner, StockLedger ledger, ICurrentUser user, TimeProvider clock)
    : IRequestHandler<DispensePrescriptionCommand, OperationResult<DispensationDto>>
{
    public const string AlreadyDispensedMessage = "Bu reçete zaten karşılandı.";

    public async Task<OperationResult<DispensationDto>> Handle(DispensePrescriptionCommand command, CancellationToken ct)
    {
        // Ucuz kontrol önce: karşılanmış reçete için Prescription servisine hiç gitmeyiz.
        if (await db.Dispensations.AnyAsync(d => d.PrescriptionId == command.PrescriptionId, ct))
            return OperationResult<DispensationDto>.Conflict(AlreadyDispensedMessage);

        var planResult = await planner.PlanAsync(command.PrescriptionId, ct);
        if (!planResult.IsSuccess) return OperationResult<DispensationDto>.Failure(planResult);

        var plan = planResult.Value!;
        if (!plan.CanDispense)
        {
            return OperationResult<DispensationDto>.Conflict(
                "Reçete karşılanamadı: bazı kalemler katalogda yok ya da stok yetersiz. Hiçbir ilaç düşülmedi.",
                plan.Lines.Where(l => l.Status != DispenseLineStatus.Matched).ToList());
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var prescription = plan.Prescription;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Fiyat anlık görüntüsü (sonradan fiyat değişse de karşılama kaydı sabit kalır).
        var medicineIds = plan.Lines.Select(l => l.MedicineId!.Value).Distinct().ToList();
        var prices = await db.Medicines.AsNoTracking()
            .Where(m => medicineIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Price, ct);

        // İlaçları hep aynı sırayla (Id'ye göre) kilitleriz: iki karşılama ters sırayla kilit isteyip birbirini beklemesin (deadlock).
        var reason = $"Reçete karşılama ({prescription.Id})";
        foreach (var group in plan.Lines.GroupBy(l => l.MedicineId!.Value).OrderBy(g => g.Key))
        {
            var required = group.First().Required;
            var change = await ledger.ApplyAsync(group.Key, -required, StockMovementType.Dispensation, reason, prescription.Id, ct);
            if (change.Outcome == StockChangeOutcome.Applied) continue;

            // Plan ile düşüm arasında başka biri stoğu azalttı (ya da ilacı sildi): transaction geri alınır, öncekiler de iptal.
            await tx.RollbackAsync(ct);
            var failed = group.Select(l => l with { Status = change.Outcome == StockChangeOutcome.NotFound ? DispenseLineStatus.NotFound : DispenseLineStatus.InsufficientStock }).ToList();
            return OperationResult<DispensationDto>.Conflict(
                "Reçete karşılanamadı: stok bu sırada değişti. Hiçbir ilaç düşülmedi.", failed);
        }

        var dispensation = new Dispensation
        {
            Id = Guid.CreateVersion7(),
            PrescriptionId = prescription.Id,
            PatientId = prescription.PatientId,
            DoctorId = prescription.DoctorId,
            DispensedBy = user.UserId,
            DispensedAt = now,
            Lines = plan.Lines.Select(l => new DispensationLine
            {
                Id = Guid.CreateVersion7(),
                LineNo = l.LineNo,
                PrescriptionMedicineName = l.PrescriptionMedicineName,
                MedicineId = l.MedicineId!.Value,
                MedicineName = l.MedicineName!,
                Quantity = DispensePlanner.QuantityPerLine,
                UnitPrice = prices[l.MedicineId!.Value],
            }).ToList(),
        };
        db.Dispensations.Add(dispensation);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            // Aynı reçeteyi aynı anda iki istek karşılamaya kalktı: PrescriptionId tekil dizini ikincisini durdurur,
            // transaction geri alınır (stoklar ikinci kez düşmez).
            await tx.RollbackAsync(ct);
            return OperationResult<DispensationDto>.Conflict(AlreadyDispensedMessage);
        }

        await tx.CommitAsync(ct);
        return OperationResult<DispensationDto>.Created(DispensationProjection.ToDto(dispensation));
    }
}
