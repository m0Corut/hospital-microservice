using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Hastane.Notification.Api.Entities;

// Uygulama içi bildirim. Alıcı ya UserId (hasta, Identity "sub") ya DoctorId (doktor) ile adreslenir; ikisinden tam biri dolu.
public class AppNotification
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string? UserId { get; set; }
    public string? DoctorId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    // Yalnız uygulama içi göreli yol ("/Patient/..."): açık yönlendirme ve javascript: bağlantısı girişte reddedilir.
    public string? Link { get; set; }

    public string Kind { get; set; } = "info";

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ReadAt { get; set; }
}
