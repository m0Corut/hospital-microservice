namespace Hastane.Analytics.Api.Dtos;

// Doktor adını Analytics BİLMEZ (başka servisin verisi); yalnız doctorId döner, ad WebUI'da doctors/batch ile eklenir.
public class TopViewedDoctorDto
{
    public string DoctorId { get; set; } = string.Empty;
    public long Views { get; set; }
}

public class TopSearchTermDto
{
    public string Term { get; set; } = string.Empty;
    public long Count { get; set; }
}

public class AnalyticsStatusDto
{
    // Kafka:BootstrapServers dolu mu (tüketici başlatıldı mı)?
    public bool KafkaEnabled { get; set; }

    // Tüketici şu an konuya bağlı ve partition atanmış mı?
    public bool KafkaConnected { get; set; }
    public string? Topic { get; set; }
    public string? GroupId { get; set; }

    // Son işlenen olayın işlenme zamanı (UTC); hiç olay yoksa null.
    public DateTime? LastProcessedAt { get; set; }

    // Bugün (İstanbul günü) işlenen olay sayısı.
    public long ProcessedToday { get; set; }
}

public class RebuildResultDto
{
    // Başa sarılan partition sayısı.
    public int PartitionsRewound { get; set; }

    // Silinen günlük toplam kaydı sayısı (görüntüleme + terim).
    public long AggregatesDeleted { get; set; }
}
