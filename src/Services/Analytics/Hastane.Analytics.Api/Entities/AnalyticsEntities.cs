using MongoDB.Bson.Serialization.Attributes;

namespace Hastane.Analytics.Api.Entities;

// Gün biçimi hep "yyyy-MM-dd" (İstanbul günü, metin): sözlük sırası = tarih sırası, "Day >= from" sorgusu indeks aralığıdır.

// Bir doktorun bir gündeki profil görüntüleme sayısı. _id = "{doctorId}|{gün}" -> upsert + $inc tek atomik işlem.
public class DoctorDailyViews
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string Day { get; set; } = string.Empty;
    public long Count { get; set; }
}

// Bir arama teriminin (küçük harf + Türkçe katlanmış) bir gündeki sayısı. _id = "{terim}|{gün}".
public class SearchTermDaily
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    public string Term { get; set; } = string.Empty;
    public string Day { get; set; } = string.Empty;
    public long Count { get; set; }
}

// İşlenmiş olayın "kuponu". _id = eventId: ikinci kez eklenmeye çalışılırsa Mongo yinelenen anahtar hatası verir = tekrar olay.
// At alanında TTL indeksi var: kayıt süre dolunca kendiliğinden silinir.
public class ProcessedEvent
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime At { get; set; }
}
