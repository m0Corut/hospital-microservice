using System.ComponentModel.DataAnnotations;

namespace Hastane.Payment.Api.Dtos;

public class InvoiceListQuery
{
    public string? PatientId { get; set; }
    public string? DoctorId { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize { get; set; } = 10;
}
