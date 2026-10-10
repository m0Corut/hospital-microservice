namespace Hastane.Pharmacy.Api.Entities;

public enum StockMovementType
{
    Initial,
    Adjustment,
    Dispensation,
}

// Stok defteri: her stok değişikliği bir satır. Stock kolonu "bugünkü toplam", bu tablo "neden o sayı" sorusunun cevabı.
public class StockMovement
{
    public Guid Id { get; set; }
    public Guid MedicineId { get; set; }
    public StockMovementType Type { get; set; }

    // +giriş / -çıkış.
    public int Delta { get; set; }

    // Bu hareketten sonraki stok (aynı transaction içinde okunur).
    public int StockAfter { get; set; }
    public required string Reason { get; set; }
    public Guid? PrescriptionId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Medicine? Medicine { get; set; }
}
