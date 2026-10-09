namespace Hastane.Payment.Api.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; }

    // Kartın yalnız marka + son 4 hanesi saklanır. Tam numara, CVV ve son kullanma tarihi hiçbir yere yazılmaz.
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }

    public string? ProviderReference { get; set; }
    public string? FailureReason { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
    // Aynı anahtarla gelen isteğin "aynı istek" olup olmadığını anlamak için gövde özeti (SHA-256, kart verisi içermez).
    public string RequestHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    // İade talebi işareti: sağlayıcı iadesi sürerken dolu, Status hâlâ Paid. Dışarıya verilmez; RefundedAt yalnız iade BİTİNCE dolar.
    public DateTime? RefundRequestedAt { get; set; }
    public DateTime? RefundedAt { get; set; }

    // Failed satırının nedeni (idempotency replay'inde ilk cevapla aynı HTTP durumunu üretmek için).
    public PaymentFailureKind? FailureKind { get; set; }

    public Invoice? Invoice { get; set; }
}
