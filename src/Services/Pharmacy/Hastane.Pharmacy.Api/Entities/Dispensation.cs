namespace Hastane.Pharmacy.Api.Entities;

// Bir reçetenin karşılandığının kaydı. PrescriptionId TEKİL: aynı reçete ikinci kez karşılanamaz (veritabanı garantisi).
public class Dispensation
{
    public Guid Id { get; set; }
    public Guid PrescriptionId { get; set; }

    // Karşılama anındaki reçete bilgisi (Prescription servisinin verisinin kopyası; sonradan değişse de kayıt sabit kalır).
    public required string PatientId { get; set; }
    public required string DoctorId { get; set; }

    public string? DispensedBy { get; set; }
    public DateTime DispensedAt { get; set; }

    public List<DispensationLine> Lines { get; set; } = [];
}

public class DispensationLine
{
    public Guid Id { get; set; }
    public Guid DispensationId { get; set; }
    public int LineNo { get; set; }

    // Reçetede yazan ad (serbest metin) ve eşleşen katalog ilacı.
    public required string PrescriptionMedicineName { get; set; }
    public Guid MedicineId { get; set; }
    public required string MedicineName { get; set; }

    // Reçete kaleminde miktar yok: her kalem 1 kutu/ambalaj sayılır.
    public int Quantity { get; set; } = 1;

    // Karşılama anındaki fiyat (ilaç fiyatı sonradan değişirse kayıt sabit kalır).
    public decimal UnitPrice { get; set; }

    public Dispensation? Dispensation { get; set; }
}
