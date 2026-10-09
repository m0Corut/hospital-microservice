using System.ComponentModel.DataAnnotations;

namespace Hastane.Prescription.Api.Dtos;

// Id ve PrescriptionId bilerek yok: Id'yi sunucu atar, PrescriptionId reçeteye bağlanırken doldurulur.
// Hem oluşturmada hem güncellemede (kalem listesini tamamen değiştirirken) kullanılır.
public class CreatePrescriptionItemDto
{
    [Required, StringLength(200)]
    public string MedicineName { get; set; } = string.Empty;
    [Required, StringLength(100)]
    public string Dosage { get; set; } = string.Empty;
    [Required, StringLength(100)]
    public string Frequency { get; set; } = string.Empty;
    [Range(1, 365)]
    public int DurationDays { get; set; }
    [StringLength(500)]
    public string? Instructions { get; set; }
}
