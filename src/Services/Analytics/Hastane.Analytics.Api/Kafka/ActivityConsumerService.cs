using System.Text.Json;
using Confluent.Kafka;
using Hastane.Analytics.Api.Dtos;
using Hastane.Analytics.Api.Services;
using Microsoft.Extensions.Options;

namespace Hastane.Analytics.Api.Kafka;

// Tüketici: "analytics" grubunda konuyu okur, her olayı işler, İŞLEDİKTEN SONRA offset'i commit eder.
//
// En az bir kez: otomatik commit KAPALI. Olay işlenip commit edilmeden süreç çökerse, yeniden başlayınca aynı olay tekrar
// gelir; ActivityProcessor eventId kuponuyla tekrarı atlar. (Önce commit, sonra işleme olsaydı çökmede olay KAYBOLURDU.)
//
// Kafka kapalıysa: Consume null döner, hata işleyicisi "bağlı değil" der; döngü sessizce bekler, broker gelince devam eder.
// Servis (HTTP uçları, Mongo'daki mevcut toplamlar) bundan etkilenmez.
public sealed class ActivityConsumerService(
    IOptions<Settings.KafkaOptions> options,
    IActivityProcessor processor,
    KafkaConnectionState state,
    ReplayCoordinator replay,
    ILogger<ActivityConsumerService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        state.Enabled = !string.IsNullOrWhiteSpace(o.BootstrapServers);
        if (!state.Enabled)
        {
            logger.LogInformation("Kafka:BootstrapServers boş: tüketici kapalı.");
            return;
        }

        await Task.Yield(); // uygulama açılışını bekletme

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConsumerAsync(o, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                state.Connected = false;
                logger.LogWarning(ex, "Kafka tüketicisi durdu, 5 sn sonra yeniden başlatılacak.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task RunConsumerAsync(Settings.KafkaOptions o, CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,
            GroupId = o.GroupId,
            // Grubun kayıtlı konumu yoksa (ilk çalışma) en baştan oku: konuda ne varsa işlensin.
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // Offset'i BİZ commit ederiz (işledikten sonra). Otomatik commit "okudum" der, "işledim" demez.
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AllowAutoCreateTopics = true,
            SocketTimeoutMs = 5_000,
        };

        var lastLoggedDown = false;
        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) =>
            {
                if (e.Code == ErrorCode.Local_AllBrokersDown)
                {
                    state.Connected = false;
                    if (!lastLoggedDown) logger.LogWarning("Kafka'ya ulaşılamıyor, bağlantı beklenecek.");
                    lastLoggedDown = true;
                }
                else
                {
                    logger.LogDebug("Kafka tüketici: {Reason}", e.Reason);
                }
            })
            .SetPartitionsAssignedHandler((_, partitions) =>
            {
                state.Connected = true;
                lastLoggedDown = false;
                logger.LogInformation("Kafka partition atandı: {Partitions}", string.Join(',', partitions.Select(p => p.Partition.Value)));
            })
            .SetPartitionsRevokedHandler((_, _) => state.Connected = false)
            .Build();

        consumer.Subscribe(o.Topic);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (replay.TryTake(out var pending))
                    await RewindAsync(consumer, pending, ct);

                // Kısa zaman aşımı: döngü yeniden oynatma taleplerini ve iptali sık yoklayabilsin.
                var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (result is null || result.IsPartitionEOF) continue;

                state.Connected = true;
                if (!await HandleAsync(consumer, result, ct)) continue;

                // İşlendi (ya da bilinçli atlandı): şimdi commit. Çökme bu satırdan ÖNCE olursa olay tekrar gelir ve kupon yakalar.
                consumer.Commit(result);
            }
        }
        finally
        {
            state.Connected = false;
            try { consumer.Close(); }
            catch (Exception ex) { logger.LogDebug(ex, "Kafka tüketici kapatılamadı."); }
        }
    }

    // true: commit edilebilir. false: işlenemedi (Mongo hatası); konum geri sarıldı, olay yeniden denenecek.
    private async Task<bool> HandleAsync(IConsumer<string, string> consumer, ConsumeResult<string, string> result, CancellationToken ct)
    {
        ActivityEvent? activity;
        try
        {
            activity = JsonSerializer.Deserialize<ActivityEvent>(result.Message.Value, Json);
        }
        catch (JsonException ex)
        {
            // Bozuk mesaj: sonsuza dek takılmayalım, günlüğe yazıp geç.
            logger.LogWarning(ex, "Çözülemeyen etkinlik atlandı: {Position}", result.TopicPartitionOffset);
            return true;
        }

        if (activity is null) return true;

        try
        {
            await processor.ProcessAsync(activity, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Etkinlik işlenemedi, yeniden denenecek: {Position}", result.TopicPartitionOffset);
            consumer.Seek(result.TopicPartitionOffset); // aynı olaya geri dön
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return false;
        }
    }

    // YENİDEN OYNATMA: toplamları sil, grubun konumunu her partition'ın en eski mesajına çek. Kafka mesajları tüketilince
    // silmez (saklama süresi dolana kadar durur), bu yüzden aynı olaylar baştan okunup toplamlar sıfırdan kurulur.
    private async Task RewindAsync(IConsumer<string, string> consumer, TaskCompletionSource<RebuildOutcome> pending, CancellationToken ct)
    {
        try
        {
            var assignment = consumer.Assignment;
            if (assignment.Count == 0)
            {
                pending.SetResult(new RebuildOutcome(false, "Henüz partition atanmadı; birkaç saniye sonra yeniden deneyin.", null));
                return;
            }

            var deleted = await processor.ClearAllAsync(ct);

            var commits = new List<TopicPartitionOffset>();
            foreach (var tp in assignment)
            {
                var low = consumer.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(5)).Low;
                consumer.Seek(new TopicPartitionOffset(tp, low));
                commits.Add(new TopicPartitionOffset(tp, low));
            }

            // Kalıcı olarak da başa al: sıfırlamadan hemen sonra süreç yeniden başlarsa eski (ileri) konumdan değil baştan okusun.
            consumer.Commit(commits);

            logger.LogInformation("Yeniden hesaplama başladı: {Partitions} partition başa sarıldı, {Deleted} toplam kaydı silindi.", commits.Count, deleted);
            pending.SetResult(new RebuildOutcome(true, null, new RebuildResultDto { PartitionsRewound = commits.Count, AggregatesDeleted = deleted }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Yeniden hesaplama başarısız.");
            pending.TrySetResult(new RebuildOutcome(false, "Yeniden hesaplama başarısız oldu.", null));
        }
    }
}
