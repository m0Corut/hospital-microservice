namespace Hastane.Patient.Api.Entities;

// DoctorId Doctor servisinin (MongoDB) kimliği; başka servisin verisi olduğundan yabancı anahtar yok, ekleme anında HTTP ile doğrulanır.
public class FavoriteDoctor
{
    public string UserId { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
