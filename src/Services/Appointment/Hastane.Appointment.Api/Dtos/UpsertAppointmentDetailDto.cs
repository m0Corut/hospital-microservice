using System.ComponentModel.DataAnnotations;

namespace Hastane.Appointment.Api.Dtos;

public class UpsertAppointmentDetailDto
{
    [Required, StringLength(1000)]
    public string Complaint { get; set; } = string.Empty;
    [StringLength(1000)]
    public string? Diagnosis { get; set; }
    [StringLength(2000)]
    public string? DoctorNotes { get; set; }
}
