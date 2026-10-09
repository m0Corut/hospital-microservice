using System.ComponentModel.DataAnnotations;
using Hastane.Prescription.Api.Validation;

namespace Hastane.Prescription.Api.Dtos;

// Id ve IssuedAt bilerek yok: ikisini de sunucu atar.
public class CreatePrescriptionDto
{
    [NotEmptyGuid]
    public Guid AppointmentId { get; set; }
    // Doktor için YOK SAYILIR: DoctorId token'daki doctor_id claim'inden gelir. Yalnız Admin başkası adına
    // reçete yazarken doldurur (controller'da zorunlu tutulur). PatientId ise gövdeden gelir: doktor hastayı seçer.
    [StringLength(64)]
    public string? DoctorId { get; set; }
    [Required, StringLength(64)]
    public string PatientId { get; set; } = string.Empty;
    [StringLength(2000)]
    public string? Notes { get; set; }
    // En az 1, en çok 20 kalem. Her kalem de kendi kurallarıyla doğrulanır.
    [Required, MinLength(1), MaxLength(20)]
    public List<CreatePrescriptionItemDto> Items { get; set; } = [];
}
