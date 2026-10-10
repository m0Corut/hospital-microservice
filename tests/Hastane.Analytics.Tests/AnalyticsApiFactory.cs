using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Testcontainers.Kafka;
using Testcontainers.MongoDb;

namespace Hastane.Analytics.Tests;

[CollectionDefinition(Name)]
public class AnalyticsCollection : ICollectionFixture<AnalyticsApiFactory>
{
    public const string Name = "analytics";
}

// Gerçek MongoDB + gerçek Kafka (confluentinc/cp-kafka, Testcontainers.Kafka modülü) konteyneri. Tüm test sınıfları
// [Collection("analytics")] ile TEK fabrikayı paylaşır: Kafka açılışı ~15-30 sn, sınıf başına açmak pahalı.
// Bu yüzden her test KENDİ benzersiz doctorId / terimini üretir; ortak veritabanında birbirinin sayısını bozmaz.
public class AnalyticsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DatabaseName = "hastane_analytics_test";
    public const string Partitions = "3";

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7").Build();
    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.5.12").Build();
    private long _produced;

    // Testler arası çakışmasın diye her çalıştırmada benzersiz konu ve grup.
    public string Topic { get; } = "doctor-activity-" + Guid.NewGuid().ToString("N")[..8];
    public string GroupId { get; } = "analytics-test-" + Guid.NewGuid().ToString("N")[..8];

    public string BootstrapServers => _kafka.GetBootstrapAddress();
    public string MongoConnectionString => _mongo.GetConnectionString();

    /// <summary>Bu fabrikanın konusuna yazılan toplam mesaj sayısı (commit edilen offset toplamıyla karşılaştırılır).</summary>
    public long ProducedCount => Interlocked.Read(ref _produced);

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_mongo.StartAsync(), _kafka.StartAsync());

        // Konuyu 3 partition ile kendimiz kurarız (sıralama testi anahtar -> partition ilişkisine bakar).
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync([new TopicSpecification { Name = Topic, NumPartitions = int.Parse(Partitions), ReplicationFactor = 1 }]);
    }

    public IMongoDatabase Database => new MongoClient(MongoConnectionString).GetDatabase(DatabaseName);

    // Ayarlar bellek içi yapılandırmayla EZİLİR (appsettings.Development.json'daki localhost:9092'yi de).
    public static void Configure(IWebHostBuilder builder, IDictionary<string, string?> settings)
        => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestJwt();
        Configure(builder, new Dictionary<string, string?>
        {
            ["DatabaseSettings:DatabaseName"] = DatabaseName,
            ["Kafka:BootstrapServers"] = BootstrapServers,
            ["Kafka:Topic"] = Topic,
            ["Kafka:GroupId"] = GroupId,
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMongoClient>();
            services.AddSingleton<IMongoClient>(_ => new MongoClient(MongoConnectionString));
        });
    }

    /// <summary>Kafka'sız / ulaşılamayan Kafka'lı kardeş ana bilgisayar (aynı Mongo). Boş = tüketici kapalı.</summary>
    public WebApplicationFactory<Program> WithKafka(string bootstrapServers)
        => WithWebHostBuilder(b => Configure(b, new Dictionary<string, string?> { ["Kafka:BootstrapServers"] = bootstrapServers }));

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Doctor servisinin yazdığı biçimde olay üretir ve teslim onayını bekler. key null olabilir (arama olayı).</summary>
    public async Task ProduceAsync(params (string? Key, object Event)[] events)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = BootstrapServers, EnableIdempotence = true }).Build();
        foreach (var (key, evt) in events)
        {
            await producer.ProduceAsync(Topic, new Message<string, string> { Key = key!, Value = JsonSerializer.Serialize(evt, Json) });
            Interlocked.Increment(ref _produced);
        }
        producer.Flush(TimeSpan.FromSeconds(10));
    }

    /// <summary>Grubun commit ettiği offset'lerin toplamı (hiç commit yoksa 0). Tüketici grubuna KATILMADAN sorgular.</summary>
    public long CommittedOffsetTotal()
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        var partitions = admin.GetMetadata(Topic, TimeSpan.FromSeconds(10)).Topics.Single().Partitions
            .Select(p => new TopicPartition(Topic, p.PartitionId)).ToList();
        using var consumer = new ConsumerBuilder<Ignore, Ignore>(new ConsumerConfig { BootstrapServers = BootstrapServers, GroupId = GroupId }).Build();
        return consumer.Committed(partitions, TimeSpan.FromSeconds(10)).Sum(o => o.Offset.IsSpecial ? 0 : o.Offset.Value);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _kafka.DisposeAsync();
        await _mongo.DisposeAsync();
    }
}
