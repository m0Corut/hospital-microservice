using System.ComponentModel.DataAnnotations;
using Hastane.Payment.Api.Entities;

namespace Hastane.Payment.Api.Dtos;

public class PaymentListQuery
{
    public string? PatientId { get; set; }
    public string? DoctorId { get; set; }
    public Guid? AppointmentId { get; set; }
    public PaymentStatus? Status { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize { get; set; } = 10;
}
