namespace Hastane.Doctor.Api.Activity;

// appsettings "Kafka" bölümü. BootstrapServers boşsa olay akışı KAPALI: hiçbir şey kanala yazılmaz, üretici hiç başlamaz.
public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;
    public string Topic { get; set; } = "hastane.doctor-activity";

    // Bellek içi kanal kapasitesi; dolarsa YENİ olay düşürülür (istek asla beklemez).
    public int ChannelCapacity { get; set; } = 10_000;

    // Konu yoksa kurulurken: 3 partition (anahtar aynıysa aynı partition), 7 gün saklama. Tek düğümde kopya sayısı 1.
    public int TopicPartitions { get; set; } = 3;
    public short ReplicationFactor { get; set; } = 1;
    public int RetentionDays { get; set; } = 7;
}
