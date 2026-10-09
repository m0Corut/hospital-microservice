using Hastane.Doctor.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using DoctorEntity = Hastane.Doctor.Api.Entities.Doctor;

namespace Hastane.Doctor.Api.Services;

// Açılışta dizinleri oluşturur. CreateOne idempotent: dizin varsa ve tanımı aynıysa hiçbir şey yapmaz.
// IMongoClient constructor'dan değil try içinde çözülür: bağlantı cümlesi boş/bozuksa istemci kurulurken fırlayan
// hata da yakalanır ve uygulama düşmez (dizin yoksa servis yavaşlar ama çalışır).
public class DoctorIndexInitializer(IServiceProvider services, ILogger<DoctorIndexInitializer> logger) : IHostedService
{
    // Mongo kapalıysa açılışı dakikalarca bekletmesin.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            var client = services.GetRequiredService<IMongoClient>();
            var settings = services.GetRequiredService<IOptions<DatabaseSettings>>().Value;
            var doctors = client.GetDatabase(settings.DatabaseName).GetCollection<DoctorEntity>(settings.DoctorCollectionName);
            var keys = Builders<DoctorEntity>.IndexKeys;

            await doctors.Indexes.CreateOneAsync(new CreateIndexModel<DoctorEntity>(keys.Ascending(d => d.BranchId),
                new CreateIndexOptions { Name = "ix_branch" }), cancellationToken: cts.Token);
            await doctors.Indexes.CreateOneAsync(new CreateIndexModel<DoctorEntity>(keys.Ascending(d => d.Gender),
                new CreateIndexOptions { Name = "ix_gender" }), cancellationToken: cts.Token);
            // Dizi içindeki alan: MongoDB her dizi öğesi için ayrı kayıt tutar (multikey dizin).
            await doctors.Indexes.CreateOneAsync(new CreateIndexModel<DoctorEntity>(keys.Ascending("ClinicLocations.City"),
                new CreateIndexOptions { Name = "ix_city" }), cancellationToken: cts.Token);

            // NOT: `text` araması regex olduğu için bu dizinlerden HİÇBİRİNİ kullanamaz (baştan sona koleksiyon taraması).
            // Büyüyünce Elasticsearch ile değişecek (B21).
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Doctor dizinleri oluşturulamadı; servis dizinsiz devam ediyor.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
