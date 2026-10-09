namespace Hastane.Payment.Api.Dtos;

// Kart bilgisi yalnız marka + son 4 hane. Tam numara/CVV/son kullanma bu modelde hiç yok.
public class PaymentResultDto
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public string? FailureReason { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? RefundedAt { get; set; }
}
