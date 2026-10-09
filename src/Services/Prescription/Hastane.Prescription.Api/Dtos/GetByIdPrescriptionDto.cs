namespace Hastane.Prescription.Api.Dtos;

public class GetByIdPrescriptionDto
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public string? Notes { get; set; }
    public List<PrescriptionItemDto> Items { get; set; } = [];
}
