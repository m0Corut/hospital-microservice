namespace Hastane.Prescription.Api.Dtos;

// Liste satırı: kalemlerin kendisi yok, yalnız sayısı (SQL'de COUNT ile hesaplanır).
public class ResultPrescriptionDto
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public int ItemCount { get; set; }
}
