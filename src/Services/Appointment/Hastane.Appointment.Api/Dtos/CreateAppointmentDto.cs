using System.ComponentModel.DataAnnotations;
using Hastane.Appointment.Api.Validation;

namespace Hastane.Appointment.Api.Dtos;

// Id, Status ve CreatedAt bilerek yok: Id'yi (Guid v7) ve CreatedAt'i sunucu atar, Status her zaman Pending başlar.
public class CreateAppointmentDto
{
    [Required, StringLength(64)]
    public string DoctorId { get; set; } = string.Empty;
    // Hasta (Patient rolü) için YOK SAYILIR: PatientId token'daki sub'dan gelir. Yalnız Admin başkası adına
    // randevu açarken doldurur (controller'da zorunlu tutulur).
    [StringLength(64)]
    public string? PatientId { get; set; }
    [Required, StringLength(64)]
    public string BranchId { get; set; } = string.Empty;
    [FutureDate]
    public DateTime AppointmentDate { get; set; }
}
