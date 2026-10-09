namespace Hastane.Payment.Api.Dtos;

// Liste satırı: kalemsiz özet.
public class ResultInvoiceDto
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

// Tek fatura: kalemlerle.
public class GetByIdInvoiceDto : ResultInvoiceDto
{
    public List<InvoiceLineDto> Lines { get; set; } = [];
}

public class InvoiceLineDto
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}
