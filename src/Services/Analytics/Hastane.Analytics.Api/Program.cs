using Hastane.Analytics.Api.Kafka;
using Hastane.Analytics.Api.Services;
using Hastane.Analytics.Api.Settings;
using Hastane.Shared.Security;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Ayarlar appsettings.json "DatabaseSettings" ve "Kafka" bölümlerinden (bağlantı cümlesi user-secrets'tan) okunur.
builder.Services.Configure<DatabaseSettings>(builder.Configuration.GetSection("DatabaseSettings"));
builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection(KafkaOptions.SectionName));

// MongoClient bağlantı havuzunu içerir, pahalıdır ve iş parçacığı güvenlidir: uygulama boyunca TEK örnek.
// Testte WebApplicationFactory bu kaydı konteyner adresiyle ezer.
builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(sp.GetRequiredService<IOptions<DatabaseSettings>>().Value.ConnectionString));

builder.Services.AddSingleton<IActivityProcessor, ActivityProcessor>();
builder.Services.AddSingleton<IAnalyticsQueryService, AnalyticsQueryService>();
builder.Services.AddHostedService<AnalyticsIndexInitializer>();

// Kafka tüketicisi: arka plan hizmeti. Kafka:BootstrapServers boşsa ya da Kafka kapalıysa servis yine ayakta kalır.
builder.Services.AddSingleton<KafkaConnectionState>();
builder.Services.AddSingleton<ReplayCoordinator>();
builder.Services.AddHostedService<ActivityConsumerService>();

// Gelen token'ı doğrulama (RS256, açık anahtar) + politikalar + "token'daki kullanıcı kim?" (ICurrentUser).
builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneAuthorization();
builder.Services.AddHastaneCurrentUser();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Sıra önemli: önce "kimsin" (token'ı çöz), sonra "iznin var mı"; ikisi de uç eşlemeden önce.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
