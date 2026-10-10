using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using Hastane.Pharmacy.Api.Integration;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Dispensing;

public sealed record DispensePlan(PrescriptionInfo Prescription, IReadOnlyList<DispenseLineDto> Lines)
{
    public bool CanDispense => Lines.Count > 0 && Lines.All(l => l.Status == DispenseLineStatus.Matched);
}

// Önizleme (sorgu) ve karşılama (komut) AYNI planı kullanır: ekranda "karşılanabilir" gören yönetici, basınca aynı kuralı çalıştırır.
// Plan yalnız OKUR; stoğa dokunmaz. Gerçek düşüm Handler'da, transaction içinde, koşullu UPDATE ile yapılır
// (plan ile düşüm arasında başka biri stoğu azaltmış olabilir; asıl koruma düşüm anındaki koşuldur).
public sealed class DispensePlanner(PharmacyDbContext db, IPrescriptionLookup prescriptions)
{
    // Reçete kaleminde miktar yok: her kalem 1 ambalaj.
    public const int QuantityPerLine = 1;

    public async Task<OperationResult<DispensePlan>> PlanAsync(Guid prescriptionId, CancellationToken ct)
    {
        var lookup = await prescriptions.GetAsync(prescriptionId, ct);
        switch (lookup.Outcome)
        {
            case LookupOutcome.NotFound:
                return OperationResult<DispensePlan>.NotFound("Reçete bulunamadı.");
            case LookupOutcome.Forbidden:
                return OperationResult<DispensePlan>.Forbidden("Reçeteye erişim izni yok.");
            case LookupOutcome.Unavailable:
                return OperationResult<DispensePlan>.ServiceUnavailable("Reçete servisine ulaşılamadı; stoğa dokunulmadı. Biraz sonra tekrar deneyin.");
        }

        var prescription = lookup.Value!;
        if (prescription.Items.Count == 0)
            return OperationResult<DispensePlan>.Conflict("Reçetede karşılanacak kalem yok.");

        var entries = prescription.Items
            .OrderBy(i => i.LineNo)
            .Select(i => (Item: i, Key: TurkishText.Fold(i.MedicineName)))
            .ToList();
        var names = entries.Select(e => e.Key).Where(k => k.Length > 0).Distinct().ToList();

        // Eşleştirme veritabanında NormalizedName üzerinden: katalog büyüse de tüm ilaçlar belleğe yüklenmez.
        var candidates = (await db.Medicines.AsNoTracking()
                .Where(m => m.IsActive && names.Contains(m.NormalizedName))
                .Select(m => new Candidate(m.Id, m.Name, m.NormalizedName, m.Stock))
                .ToListAsync(ct))
            .GroupBy(m => m.NormalizedName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Aynı ilaç reçetede iki kez yazılmışsa toplam ihtiyaç hesaplanır (ikisi de ortak stok havuzundan düşer).
        var requiredPerMedicine = entries
            .Where(e => candidates.TryGetValue(e.Key, out var c) && c.Count == 1)
            .GroupBy(e => candidates[e.Key][0].Id)
            .ToDictionary(g => g.Key, g => g.Count() * QuantityPerLine);

        var lines = new List<DispenseLineDto>();
        foreach (var (item, key) in entries)
        {
            if (key.Length == 0 || !candidates.TryGetValue(key, out var matches))
            {
                lines.Add(new(item.LineNo, item.MedicineName, null, null, QuantityPerLine, 0, DispenseLineStatus.NotFound));
            }
            else if (matches.Count > 1)
            {
                lines.Add(new(item.LineNo, item.MedicineName, null, null, QuantityPerLine, 0, DispenseLineStatus.Ambiguous));
            }
            else
            {
                var medicine = matches[0];
                var required = requiredPerMedicine[medicine.Id];
                var status = medicine.Stock >= required ? DispenseLineStatus.Matched : DispenseLineStatus.InsufficientStock;
                lines.Add(new(item.LineNo, item.MedicineName, medicine.Id, medicine.Name, required, medicine.Stock, status));
            }
        }

        return OperationResult<DispensePlan>.Ok(new DispensePlan(prescription, lines));
    }
}

internal sealed record Candidate(Guid Id, string Name, string NormalizedName, int Stock);
