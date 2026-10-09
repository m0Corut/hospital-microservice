namespace Hastane.Prescription.Api.Entities;

public class Prescription
{
    public Guid Id { get; set; }

    // Appointment servisindeki randevunun kimliği: yalnız değer, o servise bağlantı (FK, ProjectReference) yok.
    public Guid AppointmentId { get; set; }
    public required string DoctorId { get; set; }
    public required string PatientId { get; set; }

    // Sunucu atar (UTC); istemci gönderemez.
    public DateTime IssuedAt { get; set; }
    public string? Notes { get; set; }

    // Bire-çok (1—N): bir reçetenin 1-20 kalemi olur.
    public List<PrescriptionItem> Items { get; set; } = [];
}
