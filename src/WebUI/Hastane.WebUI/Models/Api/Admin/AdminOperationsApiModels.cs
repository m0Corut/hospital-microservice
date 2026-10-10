namespace Hastane.WebUI.Models.Api.Admin;

// Appointment servisi.
public sealed class AdminAppointmentDetailApiModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string Complaint { get; set; } = string.Empty;
    public string? Diagnosis { get; set; }
    public string? DoctorNotes { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class AdminAppointmentApiModel
{
    public Guid Id { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    // Hastanın randevu alırken yazdığı şikâyet / not (yoksa null).
    public string? Note { get; set; }
    // Yalnız GET /appointments/{id} dolu döner; listede yok.
    public AdminAppointmentDetailApiModel? Detail { get; set; }
}

// Prescription servisi.
public sealed class AdminPrescriptionListItemApiModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public int ItemCount { get; set; }
}

public sealed class AdminPrescriptionItemApiModel
{
    public int LineNo { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public string? Instructions { get; set; }
}

public sealed class AdminPrescriptionApiModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public string? Notes { get; set; }
    public List<AdminPrescriptionItemApiModel> Items { get; set; } = [];
}

// Payment servisi. Kart: yalnız marka + son 4 hane.
public sealed class AdminPaymentApiModel
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

public sealed class AdminInvoiceLineApiModel
{
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class AdminInvoiceApiModel
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
    // Yalnız GET /invoices/{id} dolu döner.
    public List<AdminInvoiceLineApiModel> Lines { get; set; } = [];
}
