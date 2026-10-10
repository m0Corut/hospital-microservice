using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Hastane.Chat.Api.Entities;

// Mesajlar konuşma belgesine gömülmez, ayrı koleksiyonda durur: konuşma belgesi büyümez ve
// "şu andan sonraki mesajlar" sorgusu (ConversationId, SentAt) bileşik indeksiyle ucuz kalır.
public class ChatMessage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string ConversationId { get; set; } = string.Empty;

    // "Patient" ya da "Doctor".
    public string SenderRole { get; set; } = string.Empty;

    // Gönderen kimliği (hasta için sub, doktor için doctor_id). API çıktısında yok.
    public string SenderId { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    // HAM metin: HTML kodlama (XSS) arayüzün işidir; sunucu kaçırmaz, değiştirmez (yalnız baş/son boşluk kırpılır).
    public string Text { get; set; } = string.Empty;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime SentAt { get; set; }
}
