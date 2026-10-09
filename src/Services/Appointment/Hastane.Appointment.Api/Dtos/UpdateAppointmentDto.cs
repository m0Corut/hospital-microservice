using System.ComponentModel.DataAnnotations;
using Hastane.Appointment.Api.Validation;

namespace Hastane.Appointment.Api.Dtos;

// Yalnız yeniden planlama: şube ve tarih. DoctorId, PatientId, Status ve CreatedAt DTO'da yok, değişemez
// (durum yalnız PATCH {id}/status ile, geçiş kurallarından geçerek değişir).
public class UpdateAppointmentDto
{
    public Guid Id { get; set; }
    [Required, StringLength(64)]
    public string BranchId { get; set; } = string.Empty;
    [FutureDate]
    public DateTime AppointmentDate { get; set; }
}
