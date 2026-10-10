using System.Collections.Concurrent;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;

namespace Hastane.Pharmacy.Tests;

// Tüm test sınıfları TEK konteyneri paylaşır (collection fixture); her test kendi benzersiz ilaç adı/barkodu/reçetesiyle çalışır.
// Migration'lar uygulama açılışında (Development ortamı) çalışır.
public class PharmacyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public CapturingLoggerProvider Logs { get; } = new();

    public async ValueTask InitializeAsync() => await _sql.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _sql.GetConnectionString());
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        // Üretim açık anahtarı yerine test anahtarı: TestJwt ile üretilen token'lar geçer.
        builder.UseTestJwt();
        // Zaman aşımı testi 1 sn'de biter (üretim varsayılanı 5 sn).
        builder.UseSetting("Services:TimeoutSeconds", "1");
        builder.ConfigureTestServices(services =>
            // Prescription servisi sahte: gerçek ağ yok (bkz. FakePrescriptionService).
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(FakePrescriptionService.CreateHandler)));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

// Uygulamanın yazdığı TÜM log mesajlarını toplar: "gövde loglanmaz" kontrolü için.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CaptureLogger(string category, ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => sink.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)} {exception}");
    }
}

[CollectionDefinition(Name)]
public class PharmacyCollection : ICollectionFixture<PharmacyApiFactory>
{
    public const string Name = "pharmacy-api";
}
