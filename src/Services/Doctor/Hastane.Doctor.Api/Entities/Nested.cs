using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Hastane.Doctor.Api.Entities;

public class Education
{
    public string School { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int EndYear { get; set; }
}

public class Experience
{
    public string Place { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int StartYear { get; set; }
    // Boşsa hâlâ çalışıyor demektir.
    public int? EndYear { get; set; }
}

public class Award
{
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? Description { get; set; }
}

public class ClinicLocation
{
    public string ClinicName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Address { get; set; }
    public string? Floor { get; set; }

    // Para decimal (ondalık hata yok) ve MongoDB'de Decimal128 olarak durur; double 0.1+0.2 gibi hatalar üretir.
    // IgnoreIfNull: ücreti olmayan konumda alan hiç yazılmaz; böylece "ClinicLocations.HourlyFee" sıralaması null'lara takılmaz.
    [BsonRepresentation(BsonType.Decimal128)]
    [BsonIgnoreIfNull]
    public decimal? HourlyFee { get; set; }

    // ISO 4217 üç harfli büyük harf kod (TRY, USD).
    public string Currency { get; set; } = "TRY";

    // Haftalık program: bu konumda hangi gün hangi saatlerde çalışılıyor.
    public List<ScheduleSlot> Schedule { get; set; } = [];
}

public class ScheduleSlot
{
    // Enum MongoDB'de ve JSON'da metin ("Monday"): sayı (1) okunmaz, yeniden sıralamada bozulmaz.
    [BsonRepresentation(BsonType.String)]
    public DayOfWeek DayOfWeek { get; set; }

    // "HH:mm" metin; aynı gün içinde sözlük sırası = saat sırası.
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int SlotMinutes { get; set; } = 30;
}

public class SocialLinks
{
    public string? Facebook { get; set; }
    public string? X { get; set; }
    public string? Instagram { get; set; }
    public string? LinkedIn { get; set; }
    public string? Pinterest { get; set; }
    public string? YouTube { get; set; }
}
