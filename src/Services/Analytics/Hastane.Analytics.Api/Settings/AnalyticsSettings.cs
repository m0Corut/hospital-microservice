namespace Hastane.Analytics.Api.Settings;

public class DatabaseSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string DoctorViewsCollectionName { get; set; } = "doctor_views_daily";
    public string SearchTermsCollectionName { get; set; } = "search_terms_daily";
    public string ProcessedEventsCollectionName { get; set; } = "processed_events";
}

// appsettings "Kafka" bölümü. BootstrapServers boşsa tüketici hiç başlamaz; servis yalnız Mongo'daki mevcut toplamları sunar.
public class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;
    public string Topic { get; set; } = "hastane.doctor-activity";

    // Tüketici grubu: aynı gruptaki tüketiciler partition'ları paylaşır, grubun okuma konumu (offset) Kafka'da grup adıyla saklanır.
    public string GroupId { get; set; } = "analytics";

    // İşlenmiş olay kimliklerinin saklanma süresi. Konu saklama süresinden (7 gün) UZUN olmalı: Kafka'nın tutabildiği
    // her olayın tekrarı bu süre içinde yakalanır.
    public int ProcessedEventTtlDays { get; set; } = 8;
}
