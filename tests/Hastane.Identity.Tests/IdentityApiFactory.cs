using Hastane.Identity.Api.Integration;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Hastane.Identity.Tests;

// Test sınıfı başına gerçek bir SQL Server konteyneri açar; bitince siler.
// Migration'lar ve rol tohumlama uygulama açılışında (Development) kendiliğinden çalışır.
// JWT: üretim anahtarları KULLANILMAZ. Token'ı Identity'nin kendisi TestKeys özel anahtarıyla imzalar,
// doğrulama da TestKeys açık anahtarıyla yapılır (aynı çift); TestJwt ile üretilen token'lar da geçer.
public class IdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SeedAdminEmail = "ilk-admin@example.com";
    public const string SeedAdminPassword = "Adm1nPassw0rd";

    public const string InternalApiKey = "test-internal-key-123";
    public const string WebUiBaseUrl = "http://webui.test:5010";

    // Gerçek Notification yerine: giden e-posta isteklerini kaydeder, istenirse "kapalı" gibi davranır.
    public FakeNotificationHandler Notification { get; } = new();

    // Fotoğraflar test boyunca bu geçici klasöre yazılır (gerçek %LOCALAPPDATA% kirlenmez); sınıf bitince silinir.
    public string PhotoDirectory { get; } = Path.Combine(Path.GetTempPath(), "hastane-identity-tests", Guid.NewGuid().ToString("N"));

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    // Varsayılan: ilk Admin tohumlanmaz (testler kendi Admin'ini kurar). AdminSeedApiFactory açar.
    protected virtual bool SeedAdmin => false;

    public async ValueTask InitializeAsync() => await _sql.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var cs = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "hastane_identity_test" };
        builder.UseSetting("ConnectionStrings:IdentityDb", cs.ConnectionString);

        builder.UseTestJwt();
        builder.UseTestJwtSigningKey();
        // Geliştiricinin user-secrets'ındaki Seed değerleri testi etkilemesin: her zaman açıkça ezilir.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:AdminEmail"] = SeedAdmin ? SeedAdminEmail : "",
            ["Seed:AdminPassword"] = SeedAdmin ? SeedAdminPassword : "",
            ["Internal:ApiKey"] = InternalApiKey,
            ["Services:NotificationBaseUrl"] = "http://notification.test",
            ["WebUi:BaseUrl"] = WebUiBaseUrl,
            ["Storage:PhotoPath"] = PhotoDirectory,
        }));
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<INotificationEmailClient, NotificationEmailClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Notification));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
        try
        {
            if (Directory.Exists(PhotoDirectory)) Directory.Delete(PhotoDirectory, recursive: true);
        }
        catch (IOException)
        {
            // geçici klasör; silinemezse işletim sistemi temizler
        }
    }
}

public class AdminSeedApiFactory : IdentityApiFactory
{
    protected override bool SeedAdmin => true;
}
