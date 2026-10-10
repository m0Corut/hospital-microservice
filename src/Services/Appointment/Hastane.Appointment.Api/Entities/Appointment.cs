namespace Hastane.Appointment.Api.Entities;

public class Appointment
{
    public Guid Id { get; set; }

    // Üç kimlik de yalnız metin: başka servislerin (Doctor: MongoDB, Identity, Branch) verisine bağlantı yok.
    public required string DoctorId { get; set; }
    public required string PatientId { get; set; }
    public required string BranchId { get; set; }

    public DateTime AppointmentDate { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Pending;
    public DateTime CreatedAt { get; set; }

    // Hasta şikâyeti / not. Yalnız oluştururken verilir; yeniden planlama değiştirmez.
    public string? Note { get; set; }

    // Bire-bir (0..1): randevunun muayene detayı henüz girilmemiş olabilir.
    public AppointmentDetail? Detail { get; set; }
}
