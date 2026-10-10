using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Hastane.Notification.Api.Entities;

// Gönderim günlüğü. Gövde BİLEREK saklanmaz: şifre sıfırlama e-postası tek kullanımlık bağlantı içerir.
public class EmailLog
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime SentAt { get; set; }

    public bool Success { get; set; }

    // Başarısızsa kısaltılmış hata (tür + ileti, en çok 300 karakter).
    public string? Error { get; set; }
}
