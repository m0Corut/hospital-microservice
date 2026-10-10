using Hastane.Notification.Api.Settings;
using Hastane.Notification.Api.Services;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Hastane.Notification.Tests;

// Test sınıfı başına gerçek bir MongoDB konteyneri; e-posta gönderen SAHTE (gerçek SMTP yok).
public class NotificationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DatabaseName = "hastane_notification_test";
    public const string TestInternalKey = "test-internal-key-notification";

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7").Build();

    public FakeEmailSender Email { get; } = new();

    public string MongoConnectionString => _mongo.GetConnectionString();

    // İç anahtar ayarı. Alt sınıf boş döndürerek "ayar yok" durumunu kurar.
    protected virtual string InternalKey => TestInternalKey;

    public async ValueTask InitializeAsync() => await _mongo.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestJwt();
        builder.UseSetting("DatabaseSettings:DatabaseName", DatabaseName);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMongoClient>();
            services.AddSingleton<IMongoClient>(_ => new MongoClient(_mongo.GetConnectionString()));
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            // user-secrets'taki gerçek anahtar testi etkilemesin: seçenek son söz olarak sabit değere çekilir.
            services.PostConfigure<InternalOptions>(o => o.ApiKey = InternalKey);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _mongo.DisposeAsync();
    }
}

// Internal:ApiKey ayarı BOŞ olan servis: iç uçlar her koşulda 401 vermeli.
public class NoKeyNotificationApiFactory : NotificationApiFactory
{
    protected override string InternalKey => string.Empty;
}
