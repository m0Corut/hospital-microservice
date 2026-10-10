using Hastane.Notification.Api.Entities;
using Hastane.Notification.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Hastane.Notification.Api.Services;

// Açılışta alıcı sorguları için indeksleri kurar (CreateMany aynı indekste zararsızdır). Mongo o an yoksa servis
// yine açılır: indeks eksikliği yavaşlık demektir, hata değil.
public class MongoIndexInitializer(
    IMongoClient client, IOptions<DatabaseSettings> options, ILogger<MongoIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = options.Value;
            var collection = client.GetDatabase(settings.DatabaseName)
                .GetCollection<AppNotification>(settings.NotificationCollectionName);
            var keys = Builders<AppNotification>.IndexKeys;
            await collection.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<AppNotification>(keys.Ascending(n => n.UserId).Descending(n => n.CreatedAt)),
                new CreateIndexModel<AppNotification>(keys.Ascending(n => n.DoctorId).Descending(n => n.CreatedAt)),
                // İdempotent olay tüketimi: SourceKey yalnız dolu belgelerde tekil (REST ile oluşan bildirimlerde alan null).
                new CreateIndexModel<AppNotification>(keys.Ascending(n => n.SourceKey), new CreateIndexOptions<AppNotification>
                {
                    Unique = true,
                    Name = "UX_Notifications_SourceKey",
                    PartialFilterExpression = new BsonDocumentFilterDefinition<AppNotification>(
                        new BsonDocument("SourceKey", new BsonDocument("$type", "string"))),
                }),
            ], cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Bildirim indeksleri kurulamadı: {ExceptionType}", ex.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
