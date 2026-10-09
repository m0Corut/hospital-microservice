using System.ComponentModel.DataAnnotations;
using Hastane.Prescription.Api.Validation;

namespace Hastane.Prescription.Api.Dtos;

// DoctorId, PatientId, AppointmentId ve IssuedAt bilerek yok: reçete yazıldıktan sonra değişmez.
// Items eski kalemlerin YERİNE geçer (eskiler silinir, bunlar eklenir).
public class UpdatePrescriptionDto
{
    [NotEmptyGuid]
    public Guid Id { get; set; }
    [StringLength(2000)]
    public string? Notes { get; set; }
    [Required, MinLength(1), MaxLength(20)]
    public List<CreatePrescriptionItemDto> Items { get; set; } = [];
}
