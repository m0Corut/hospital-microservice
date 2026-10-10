using Microsoft.Extensions.Options;

namespace Hastane.Doctor.Api.Search;

public static class SearchServiceCollectionExtensions
{
    // Elasticsearch:Url ayarına ÇÖZÜMLEME ANINDA bakılır (Program.cs'te değil): testlerde WebApplicationFactory ayarları
    // sonradan ezer. Url boşsa NullDoctorSearchIndex: servis yalnız MongoDB ile, hiçbir dış bağımlılık olmadan çalışır.
    public static IServiceCollection AddDoctorSearch(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ElasticsearchOptions>(configuration.GetSection("Elasticsearch"));
        services.AddSingleton<IDoctorSearchIndex>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ElasticsearchOptions>>();
            return string.IsNullOrWhiteSpace(options.Value.Url)
                ? new NullDoctorSearchIndex()
                : new ElasticDoctorSearchIndex(options, sp.GetRequiredService<ILogger<ElasticDoctorSearchIndex>>());
        });
        services.AddHostedService<DoctorSearchIndexInitializer>();
        return services;
    }
}

// Açılışta dizini (yoksa) kurar. Elasticsearch kapalıysa servis yine açılır (best-effort); dizin ilk başarılı yazmada/aramada kurulur.
public sealed class DoctorSearchIndexInitializer(IDoctorSearchIndex index) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (index is ElasticDoctorSearchIndex es) await es.TryEnsureIndexAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
