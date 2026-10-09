using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Hastane.Review.Tests;

// Test sınıfı başına gerçek bir MongoDB konteyneri açar; bitince siler.
public class ReviewApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7").Build();

    // Ham belge doğrulaması ve eski (ReviewerId'siz) belge eklemek için: API'yi atlayıp veritabanına doğrudan bakılır.
    public string MongoConnectionString => _mongo.GetConnectionString();

    public async ValueTask InitializeAsync() => await _mongo.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Üretim açık anahtarı yerine test anahtarı: TestJwt ile üretilen token'lar doğrulanır.
        builder.UseTestJwt();
        builder.UseSetting("DatabaseSettings:DatabaseName", "hastane_review_test");
        builder.UseSetting("DatabaseSettings:ReviewCollectionName", "reviews");
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
