using Hastane.Analytics.Api.Entities;
using Hastane.Analytics.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Hastane.Analytics.Api.Services;

// Açılışta indeksleri kurar (aynı tanımla tekrar çağrılırsa zararsızdır).
//  - processed_events.At TTL: kupon ProcessedEventTtlDays sonra kendiliğinden silinir (koleksiyon sonsuza dek büyümez).
//  - Day indeksleri: "son N gün" sorgusu indeks aralığı taramasıdır.
public class AnalyticsIndexInitializer(
    IMongoClient client,
    IOptions<DatabaseSettings> db,
    IOptions<KafkaOptions> kafka,
    ILogger<AnalyticsIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var s = db.Value;
            var database = client.GetDatabase(s.DatabaseName);

            await database.GetCollection<ProcessedEvent>(s.ProcessedEventsCollectionName).Indexes.CreateOneAsync(
                new CreateIndexModel<ProcessedEvent>(
                    Builders<ProcessedEvent>.IndexKeys.Ascending(x => x.At),
                    new CreateIndexOptions { Name = "ttl_at", ExpireAfter = TimeSpan.FromDays(Math.Max(1, kafka.Value.ProcessedEventTtlDays)) }),
                cancellationToken: ct);

            await database.GetCollection<DoctorDailyViews>(s.DoctorViewsCollectionName).Indexes.CreateOneAsync(
                new CreateIndexModel<DoctorDailyViews>(Builders<DoctorDailyViews>.IndexKeys.Ascending(x => x.Day), new CreateIndexOptions { Name = "ix_day" }),
                cancellationToken: ct);

            await database.GetCollection<SearchTermDaily>(s.SearchTermsCollectionName).Indexes.CreateOneAsync(
                new CreateIndexModel<SearchTermDaily>(Builders<SearchTermDaily>.IndexKeys.Ascending(x => x.Day), new CreateIndexOptions { Name = "ix_day" }),
                cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mongo açılışta kapalıysa servis yine ayağa kalksın.
            logger.LogError(ex, "Analytics indeksleri kurulamadı.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
