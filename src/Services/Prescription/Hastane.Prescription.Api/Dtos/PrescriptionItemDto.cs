namespace Hastane.Prescription.Api.Dtos;

public class PrescriptionItemDto
{
    public Guid Id { get; set; }
    public int LineNo { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public string? Instructions { get; set; }
}
