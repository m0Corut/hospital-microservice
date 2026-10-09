namespace Hastane.Appointment.Api.Dtos;

public class AppointmentDetailDto
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string Complaint { get; set; } = string.Empty;
    public string? Diagnosis { get; set; }
    public string? DoctorNotes { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
