namespace Hastane.Appointment.Api.Entities;

public class AppointmentDetail
{
    public Guid Id { get; set; }

    // Yabancı anahtar: bu detay hangi randevuya ait.
    public Guid AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }

    public required string Complaint { get; set; }
    public string? Diagnosis { get; set; }
    public string? DoctorNotes { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
