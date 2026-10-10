namespace Hastane.WebUI.Models.Api.Patient;

// Randevu. Status metin gelir: Pending / Confirmed / Cancelled / Completed.
public class AppointmentApiModel
{
    public Guid Id { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    // Hastanın randevu alırken yazdığı şikâyet / not (en çok 500 karakter; yoksa null).
    public string? Note { get; set; }
}

public sealed class AppointmentDetailApiModel : AppointmentApiModel
{
    public AppointmentVisitApiModel? Detail { get; set; }
}

public sealed class AppointmentVisitApiModel
{
    public string Complaint { get; set; } = string.Empty;
    public string? Diagnosis { get; set; }
    public string? DoctorNotes { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

// PatientId gönderilmez: hasta için sunucu token'dan alır.
public sealed record CreateAppointmentRequest(string DoctorId, string BranchId, DateTime AppointmentDate, string? Note = null);

// GET appointments/availability: doktorun o gün için boş slotları. Start/End UTC; saat gösterimi AppointmentTime ile İstanbul'a çevrilir.
public sealed class AvailableSlotApiModel
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string ClinicName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public sealed record UpdateAppointmentStatusRequest(string Status);

public sealed class PrescriptionApiModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public int ItemCount { get; set; }
}

public sealed class PrescriptionDetailApiModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public string? Notes { get; set; }
    public List<PrescriptionItemApiModel> Items { get; set; } = [];
}

public sealed class PrescriptionItemApiModel
{
    public int LineNo { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public string? Instructions { get; set; }
}

public sealed class PagedPatientApiModel<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// Kart bilgisi yalnız marka + son 4 hane gelir. Status: Pending / Paid / Failed / Refunded.
public sealed class PaymentApiModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
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
}

public class InvoiceApiModel
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid PaymentId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
}

public sealed class InvoiceDetailApiModel : InvoiceApiModel
{
    public List<InvoiceLineApiModel> Lines { get; set; } = [];
}

public sealed class InvoiceLineApiModel
{
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

// Ödeme gövdesi. PatientId yok (token'dan). Kart verisi yalnız bu istekte yaşar; WebUI saklamaz, loglamaz.
public sealed record CreatePaymentRequest(
    Guid AppointmentId, string DoctorId, decimal Amount, string Currency, string? Description, PaymentCardRequest Card);

public sealed record PaymentCardRequest(string Number, int ExpMonth, int ExpYear, string Cvv, string HolderName)
{
    // Kayıt (record) ToString'i tüm alanları basar; yanlışlıkla loglanırsa numara çıkmasın.
    public override string ToString() => "PaymentCardRequest(***)";
}
