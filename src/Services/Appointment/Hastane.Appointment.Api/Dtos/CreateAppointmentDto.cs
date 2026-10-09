using System.ComponentModel.DataAnnotations;
using Hastane.Appointment.Api.Validation;

namespace Hastane.Appointment.Api.Dtos;

// Id, Status ve CreatedAt bilerek yok: Id'yi (Guid v7) ve CreatedAt'i sunucu atar, Status her zaman Pending başlar.
public class CreateAppointmentDto
{
    [Required, StringLength(64)]
    public string DoctorId { get; set; } = string.Empty;
    [Required, StringLength(64)]
    public string PatientId { get; set; } = string.Empty;
    [Required, StringLength(64)]
    public string BranchId { get; set; } = string.Empty;
    [FutureDate]
    public DateTime AppointmentDate { get; set; }
}
