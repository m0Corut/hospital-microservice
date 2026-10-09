namespace Hastane.Prescription.Api.Entities;

public class PrescriptionItem
{
    public Guid Id { get; set; }
    public Guid PrescriptionId { get; set; }

    // İleride Pharmacy servisindeki ilaçla ilişkilendirilecek; şimdilik serbest metin.
    // Kalemin reçetedeki sırası (1..n). Sırayı veritabanının tesadüfüne bırakmamak için açık kolon.
    public int LineNo { get; set; }
    public required string MedicineName { get; set; }
    public required string Dosage { get; set; }
    public required string Frequency { get; set; }
    public int DurationDays { get; set; }
    public string? Instructions { get; set; }

    public Prescription? Prescription { get; set; }
}
