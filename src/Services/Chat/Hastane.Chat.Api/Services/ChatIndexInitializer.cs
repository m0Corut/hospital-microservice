using Hastane.Chat.Api.Entities;
using Hastane.Chat.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Hastane.Chat.Api.Services;

// Açılışta indeksleri kurar (aynı tanımla tekrar çağrılırsa zararsızdır). Sorgu planı:
//  - tekil (PatientUserId, DoctorId): "tek konuşma" kuralını veritabanı garanti eder (yarışta iki istek açsa biri reddedilir).
//  - (PatientUserId, LastMessageAt) ve (DoctorId, LastMessageAt): "konuşmalarım, en yeniler üstte" sıralamayı indeksten verir.
//  - (ConversationId, SentAt): sorgulamayla çalışan arayüzün "şu andan sonraki mesajlar" sorusu indeks aralığı taramasıdır.
public class ChatIndexInitializer(IMongoClient client, IOptions<DatabaseSettings> options, ILogger<ChatIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var s = options.Value;
            var db = client.GetDatabase(s.DatabaseName);
            var conversations = db.GetCollection<Conversation>(s.ConversationCollectionName);
            var messages = db.GetCollection<ChatMessage>(s.MessageCollectionName);
            var ci = Builders<Conversation>.IndexKeys;

            await conversations.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Conversation>(ci.Ascending(c => c.PatientUserId).Ascending(c => c.DoctorId),
                    new CreateIndexOptions { Unique = true, Name = "ux_patient_doctor" }),
                new CreateIndexModel<Conversation>(ci.Ascending(c => c.PatientUserId).Descending(c => c.LastMessageAt),
                    new CreateIndexOptions { Name = "ix_patient_last" }),
                new CreateIndexModel<Conversation>(ci.Ascending(c => c.DoctorId).Descending(c => c.LastMessageAt),
                    new CreateIndexOptions { Name = "ix_doctor_last" }),
            ], ct);

            await messages.Indexes.CreateOneAsync(
                new CreateIndexModel<ChatMessage>(Builders<ChatMessage>.IndexKeys.Ascending(m => m.ConversationId).Ascending(m => m.SentAt),
                    new CreateIndexOptions { Name = "ix_conversation_sent" }), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mongo açılışta kapalıysa servis yine ayağa kalksın; indeksler bir sonraki açılışta kurulur.
            logger.LogError(ex, "Chat indeksleri kurulamadı.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
