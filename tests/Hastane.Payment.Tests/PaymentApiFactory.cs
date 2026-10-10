using System.Collections.Concurrent;
using Hastane.Payment.Api.Providers;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;

namespace Hastane.Payment.Tests;

// Tüm test sınıfları TEK konteyneri paylaşır (collection fixture); her test kendi randevu/hasta kimlikleriyle çalışır.
// Migration'lar uygulama açılışında (Development ortamı) çalışır.
public class PaymentApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public CountingPaymentProvider Provider => Services.GetRequiredService<CountingPaymentProvider>();
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
        {
            // Appointment ve Doctor servisleri sahte: gerçek ağ yok (bkz. FakeDownstream).
            services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(FakeDownstream.CreateHandler));
            // Sahte sağlayıcıyı, çağrıları sayan sürümle değiştir: "sağlayıcı kaç kez çağrıldı" sorusu test edilebilir.
            services.RemoveAll<IPaymentProvider>();
            services.AddSingleton<CountingPaymentProvider>();
            services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<CountingPaymentProvider>());
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }
}

public enum RefundMode
{
    Succeed,
    Reject,
    Throw,
}

// Gerçek mock sağlayıcıyı sarar; çağrıları sayar, yarış durumlarını görünür kılmak için gecikme ekler
// ve hata enjeksiyonu sunar (charge istisnası, iade reddi/istisnası). Testler serideştir; her test sonunda Reset() çağırır.
public sealed class CountingPaymentProvider : IPaymentProvider
{
    private readonly MockPaymentProvider _inner = new();
    private int _charges;
    private int _refunds;

    public int ChargeCalls => Volatile.Read(ref _charges);
    public int RefundCalls => Volatile.Read(ref _refunds);
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(150);
    public Exception? ChargeException { get; set; }
    public RefundMode RefundMode { get; set; } = RefundMode.Succeed;
    public Action? OnChargeStarted { get; set; }
    public TimeSpan RefundDelay { get; set; } = TimeSpan.Zero;

    public void Reset()
    {
        ChargeException = null;
        RefundMode = RefundMode.Succeed;
        OnChargeStarted = null;
        RefundDelay = TimeSpan.Zero;
        Delay = TimeSpan.FromMilliseconds(150);
    }

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _charges);
        OnChargeStarted?.Invoke();
        await Task.Delay(Delay, ct);
        if (ChargeException is not null) throw ChargeException;
        return await _inner.ChargeAsync(request, ct);
    }

    public async Task<RefundResult> RefundAsync(string providerReference, decimal amount, string currency, CancellationToken ct)
    {
        Interlocked.Increment(ref _refunds);
        if (RefundDelay > TimeSpan.Zero) await Task.Delay(RefundDelay, ct);
        return RefundMode switch
        {
            RefundMode.Reject => new RefundResult(false, "Sağlayıcı iadeyi reddetti (test)"),
            RefundMode.Throw => throw new InvalidOperationException("iade patladı (test)"),
            _ => await _inner.RefundAsync(providerReference, amount, currency, ct),
        };
    }
}

// Uygulamanın yazdığı TÜM log mesajlarını ve istisna metinlerini toplar: kart verisi sızıntısı testi için.
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
public class PaymentCollection : ICollectionFixture<PaymentApiFactory>
{
    public const string Name = "payment-api";
}
