using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Hastane.Chat.Api.Entities;

// Bir hasta ile bir doktor arasındaki TEK konuşma. (PatientUserId, DoctorId) çifti tekil indekslidir.
public class Conversation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    // Identity kullanıcı kimliği (token "sub"). Düz metin, ObjectId değil.
    public string PatientUserId { get; set; } = string.Empty;

    // Açıldığı andaki görünen ad (anlık görüntü).
    public string PatientName { get; set; } = string.Empty;

    // Doctor servisindeki doktor kaydı kimliği (doktor kullanıcısında token "doctor_id").
    public string DoctorId { get; set; } = string.Empty;

    // "Unvan Ad Soyad" (anlık görüntü).
    public string DoctorName { get; set; } = string.Empty;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    // Hiç mesaj yokken CreatedAt ile aynı: liste sıralaması hep dolu bir alana dayanır.
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastMessageAt { get; set; }

    public string? LastMessagePreview { get; set; }

    // Okunmamış sayaçları. Yalnız $inc (atomik) ile artar, "read" ile sıfırlanır; uygulama tarafında okuyup-yazma yok.
    public int PatientUnread { get; set; }
    public int DoctorUnread { get; set; }
}
