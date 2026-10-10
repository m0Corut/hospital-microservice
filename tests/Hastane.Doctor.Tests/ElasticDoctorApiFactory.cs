using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Hastane.Doctor.Tests;

// Gerçek MongoDB + gerçek Elasticsearch 8.15 konteyneri. Elasticsearch modülü (Testcontainers.Elasticsearch) varsayılan olarak
// TLS + parola ister; bu testler güvenliği KAPALI düz HTTP ister (üretimde de aynı istemci ayarı: yalnız Url), bu yüzden
// genel ContainerBuilder kullanılır. 512 MB heap: CI/yerel Docker belleğini taşırmasın.
// Tüm Elasticsearch testleri TEK koleksiyonda bu fabrikayı paylaşır (konteyner başlatma ~30 sn; sınıf başına açmak pahalı).
public class ElasticDoctorApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DatabaseName = "hastane_doctor_es_test";
    public const string CollectionName = "doctors";
    public const string IndexName = "hastane-doctors-test";
    public const string EsImage = "docker.elastic.co/elasticsearch/elasticsearch:8.15.0";

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7").Build();

    private readonly IContainer _es = new ContainerBuilder(EsImage)
        .WithPortBinding(9200, true)
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("ingest.geoip.downloader.enabled", "false")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
            .ForPort(9200).ForPath("/_cluster/health").ForStatusCode(System.Net.HttpStatusCode.OK)))
        .Build();

    public string ElasticUrl => $"http://{_es.Hostname}:{_es.GetMappedPublicPort(9200)}";

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_mongo.StartAsync(), _es.StartAsync());
    }

    public IMongoCollection<BsonDocument> RawCollection
        => new MongoClient(_mongo.GetConnectionString()).GetDatabase(DatabaseName).GetCollection<BsonDocument>(CollectionName);

    // Elasticsearch AÇIK, aynı Mongo: iki yolu karşılaştıran testler bunun ES'siz kardeşini (WithoutElastic) kullanır.
    public WebApplicationFactory<Program> WithoutElastic() => WithWebHostBuilder(b => b.UseSetting("Elasticsearch:Url", ""));

    // Ulaşılamayan adres: bağlantı hemen reddedilir; servis Mongo'ya düşmeli, yazmalar yine başarılı olmalı.
    public WebApplicationFactory<Program> WithBrokenElastic()
        => WithWebHostBuilder(b => b.UseSetting("Elasticsearch:Url", "http://127.0.0.1:1").UseSetting("Elasticsearch:TimeoutSeconds", "1"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestJwt();
        builder.UseSetting("DatabaseSettings:DatabaseName", DatabaseName);
        builder.UseSetting("DatabaseSettings:DoctorCollectionName", CollectionName);
        builder.UseSetting("Elasticsearch:Url", ElasticUrl);
        builder.UseSetting("Elasticsearch:Index", IndexName);
        builder.UseSetting("Kafka:BootstrapServers", ""); // gerçek Kafka'ya olay yazılmasın
        builder.UseSetting("Elasticsearch:RefreshOnWrite", "true");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMongoClient>();
            services.AddSingleton<IMongoClient>(_ => new MongoClient(_mongo.GetConnectionString()));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _es.DisposeAsync();
        await _mongo.DisposeAsync();
    }
}

[CollectionDefinition("elastic")]
public class ElasticCollection : ICollectionFixture<ElasticDoctorApiFactory>;
