using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Hastane.Doctor.Tests;

// Test sınıfı başına gerçek bir MongoDB konteyneri açar; bitince siler.
public class DoctorApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DatabaseName = "hastane_doctor_test";
    public const string CollectionName = "doctors";

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7").Build();

    public async ValueTask InitializeAsync() => await _mongo.StartAsync();

    // Ham belge ve dizin kontrolü için: API'yi atlayıp veritabanına doğrudan bakar.
    public IMongoCollection<BsonDocument> RawCollection
        => new MongoClient(_mongo.GetConnectionString()).GetDatabase(DatabaseName).GetCollection<BsonDocument>(CollectionName);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Üretim açık anahtarı yerine test anahtarı: TestJwt ile üretilen token'lar doğrulanır.
        builder.UseTestJwt();
        builder.UseSetting("DatabaseSettings:DatabaseName", DatabaseName);
        builder.UseSetting("DatabaseSettings:DoctorCollectionName", CollectionName);
        // Elasticsearch varsayılan KAPALI: appsettings.Development.json localhost:9200 yazsa da mevcut testler yalnız Mongo ile koşar.
        builder.UseSetting("Elasticsearch:Url", "");
        // Olay akışı (Kafka) varsayılan KAPALI: appsettings.Development.json localhost:9092 yazsa da testler gerçek Kafka'ya olay yazmaz.
        builder.UseSetting("Kafka:BootstrapServers", "");
        // Singleton IMongoClient kaydını konteynerin adresiyle değiştir.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMongoClient>();
            services.AddSingleton<IMongoClient>(_ => new MongoClient(_mongo.GetConnectionString()));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _mongo.DisposeAsync();
    }
}
