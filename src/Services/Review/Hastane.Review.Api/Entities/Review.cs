using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Hastane.Review.Api.Entities;

// Veritabanındaki kaydın birebir şekli: bir hastanın bir doktora yazdığı yorum ve puan.
public class Review
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    // Doctor servisindeki doktor kimliği. Yalnız metin: Doctor'a bağlantı/çağrı yok (veri sahipliği Doctor'da).
    public string DoctorId { get; set; } = string.Empty;

    public string ReviewerName { get; set; } = string.Empty;

    // 1-5 aralığı DTO'da doğrulanır (CreateReviewDto / UpdateReviewDto).
    public int Rating { get; set; }

    public string? Comment { get; set; }

    // Sunucu atar (UTC). Kind=Utc: Mongo'dan okurken yerel saate kaymasın.
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }
}
