using Hastane.Appointment.Api.Entities;

namespace Hastane.Appointment.Api.Dtos;

public class ResultAppointmentDto
{
    public Guid Id { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public AppointmentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
}
