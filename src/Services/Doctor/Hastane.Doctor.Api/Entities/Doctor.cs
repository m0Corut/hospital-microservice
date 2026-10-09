using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Hastane.Doctor.Api.Entities;

// Veritabanındaki kaydın birebir şekli. Eğitim, deneyim, ödül ve lokasyon listeleri
// MongoDB'de doktorun belgesinin İÇİNDE durur: tek kayıt, join yok.
public class Doctor
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    // Ör. "Uzm. Dr."
    public string? Title { get; set; }

    // Branch servisindeki branş kimliği. Yalnız metin: Branch'e bağlantı/çağrı yok (veri sahipliği Branch'te).
    public string BranchId { get; set; } = string.Empty;

    public string? Biography { get; set; }
    public string? ImageUrl { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    public List<Education> Educations { get; set; } = [];
    public List<Experience> Experiences { get; set; } = [];
    public List<Award> Awards { get; set; } = [];
    public List<ClinicLocation> ClinicLocations { get; set; } = [];
}
