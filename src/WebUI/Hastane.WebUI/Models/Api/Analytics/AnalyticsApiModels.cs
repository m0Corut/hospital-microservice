namespace Hastane.WebUI.Models.Api.Analytics;

// Analytics servisi GET /api/analytics/doctors/top-viewed satırı. Doktor adı yok (Analytics bilmez); doctors/batch ile eklenir.
public sealed class TopViewedDoctorApiModel
{
    public string DoctorId { get; set; } = string.Empty;
    public long Views { get; set; }
}

// GET /api/analytics/search/top-terms satırı.
public sealed class TopSearchTermApiModel
{
    public string Term { get; set; } = string.Empty;
    public long Count { get; set; }
}

// GET /api/analytics/status.
public sealed class AnalyticsStatusApiModel
{
    public bool KafkaEnabled { get; set; }
    public bool KafkaConnected { get; set; }
    public string? Topic { get; set; }
    public string? GroupId { get; set; }
    public DateTime? LastProcessedAt { get; set; }
    public long ProcessedToday { get; set; }
}

// POST /api/analytics/rebuild yanıtı.
public sealed class AnalyticsRebuildApiModel
{
    public int PartitionsRewound { get; set; }
    public long AggregatesDeleted { get; set; }
}
