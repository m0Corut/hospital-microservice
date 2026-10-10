using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Options;

namespace Hastane.Doctor.Api.Activity;

// Arka plan üretici: kanaldan okur, Kafka'ya yazar. İstek yolundan TAMAMEN ayrı.
// Kafka kapalıysa: librdkafka mesajları kendi kuyruğunda tutup yeniden dener (message.timeout.ms dolana kadar), süre dolarsa
// teslim raporu hata verir ve loglanır. Hiçbiri doktor isteğine yansımaz. Üretici nesnesi oluşturmak bağlanmayı beklemez.
public sealed class DoctorActivityProducerService(
    ChannelDoctorActivityPublisher publisher,
    IOptions<KafkaOptions> options,
    ILogger<DoctorActivityProducerService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private long _failedDeliveries;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!publisher.Enabled)
        {
            logger.LogInformation("Kafka:BootstrapServers boş: doktor etkinlik akışı kapalı.");
            return;
        }

        await Task.Yield(); // uygulama açılışını bekletme
        var o = options.Value;

        var config = new ProducerConfig
        {
            BootstrapServers = o.BootstrapServers,
            // Aynı mesajın ağ yeniden denemesiyle çift yazılmasını önler (broker tarafında sıra numarasıyla).
            EnableIdempotence = true,
            Acks = Acks.All,
            LingerMs = 50, // küçük olayları toplu gönder
            MessageTimeoutMs = 20_000,
            SocketTimeoutMs = 5_000,
        };

        using var producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => logger.LogDebug("Kafka üretici: {Reason}", e.Reason))
            .Build();

        _ = EnsureTopicAsync(o, stoppingToken);

        try
        {
            await foreach (var activity in publisher.Reader.ReadAllAsync(stoppingToken))
                await SendAsync(producer, o.Topic, activity, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // kapanış
        }
        finally
        {
            try { producer.Flush(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) { logger.LogDebug(ex, "Kafka üretici boşaltılamadı."); }
        }
    }

    private async Task SendAsync(IProducer<string, string> producer, string topic, DoctorActivityEvent activity, CancellationToken ct)
    {
        var message = new Message<string, string>
        {
            // Anahtar = doctorId: aynı doktorun olayları aynı partition'a düşer, kendi içinde sıralı kalır.
            // Aramada doktor yok: anahtar null, istemci olayları partition'lara dağıtır.
            Key = activity.DoctorId!,
            Value = JsonSerializer.Serialize(activity, Json),
        };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                producer.Produce(topic, message, OnDelivered);
                return;
            }
            catch (ProduceException<string, string> ex) when (ex.Error.Code == ErrorCode.Local_QueueFull)
            {
                // Kafka uzun süredir kapalı, iç kuyruk dolu: biraz bekle, hâlâ doluysa olayı bırak.
                await Task.Delay(200, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Etkinlik Kafka'ya verilemedi.");
                return;
            }
        }
    }

    private void OnDelivered(DeliveryReport<string, string> report)
    {
        if (!report.Error.IsError) return;
        var failed = Interlocked.Increment(ref _failedDeliveries);
        if (failed == 1 || failed % 1000 == 0)
            logger.LogWarning("Kafka'ya yazılamayan etkinlik (toplam {Failed}): {Reason}", failed, report.Error.Reason);
    }

    // Konu yoksa 3 partition + 7 gün saklamayla kurar. Kafka kapalıysa arada bir yeniden dener; başarısızlık hiçbir şeyi durdurmaz.
    private async Task EnsureTopicAsync(KafkaOptions o, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = o.BootstrapServers, SocketTimeoutMs = 5_000 }).Build();
                await admin.CreateTopicsAsync(
                [
                    new TopicSpecification
                    {
                        Name = o.Topic,
                        NumPartitions = o.TopicPartitions,
                        ReplicationFactor = o.ReplicationFactor,
                        Configs = new Dictionary<string, string> { ["retention.ms"] = TimeSpan.FromDays(o.RetentionDays).TotalMilliseconds.ToString("0") },
                    },
                ], new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
                return;
            }
            catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Kafka konusu kurulamadı, daha sonra yeniden denenecek.");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
