using System.Text.Json.Serialization;
using Hastane.Order.Api.Data;
using Hastane.Order.Api.Integration;
using Hastane.Order.Api.Mapping;
using Hastane.Order.Api.Services;
using Hastane.Order.Api.Settings;
using Hastane.Shared.Hosting;
using Hastane.Shared.Security;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    // Enum JSON'da "Received" gibi metin olarak gelir/gider; sayı (ör. 7) kabul edilmez, 400 olur.
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(OrderMappingConfig).Assembly);
builder.Services.AddMapster();

// DbContext Scoped (istek başına bir örnek). Bağlantı cümlesi user-secrets'tan; seçenek tembel okunur ki
// testte WebApplicationFactory'nin verdiği konteyner adresi geçerli olsun.
// Sağlayıcı: Oracle MySql.EntityFrameworkCore (Pomelo'nun EF Core 10 kararlı sürümü yok), UseMySQL (büyük harf) ile.
builder.Services.AddDbContext<OrderDbContext>((sp, options) =>
    options.UseMySQL(Utf8(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!))
        // Sağlayıcının migration kilidi MariaDB 11.4'te çöküyor (GET_LOCK NULL -> InvalidCastException): NULL-güvenli sürümü tak.
        .ReplaceService<IHistoryRepository, MariaDbHistoryRepository>());

// Kimlik doğrulama (token geçerli mi?) + yetkilendirme (politikalar) + "token'daki kullanıcı kim?" (ICurrentUser).
builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneAuthorization();
builder.Services.AddHastaneCurrentUser();

// Market ile konuşma: adres/zaman aşımı "Services" ayarından, iç anahtar "Internal:ApiKey"; ikisi de tembel okunur.
builder.Services.AddOptions<DownstreamServicesOptions>()
    .BindConfiguration(DownstreamServicesOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<InternalOptions>().BindConfiguration(InternalOptions.SectionName);
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IMarketClient, MarketClient>((sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value;
    client.BaseAddress = new Uri(o.MarketBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
});

builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Development'ta ya da Database:MigrateOnStartup=true verilince (Docker ortamı).
if (app.ShouldMigrateOnStartup())
{
    // Bekleyen migration'ları açılışta uygular (üretimde veritabanı şemasını
    // uygulama değil, kontrollü bir dağıtım adımı değiştirmeli).
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.Migrate();
}

// Sıra önemli: önce kimlik (token okunur), sonra yetki (politika uygulanır), en son uç.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// MariaDB sunucusunda bağlantı karakter kümesi varsayılan olarak latin1 gelebilir: "Ateş" sessizce "Ates" olur.
// Bağlantı cümlesinde belirtilmemişse utf8mb4 zorlanır.
static string Utf8(string connectionString)
{
    var builder = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(connectionString);
    if (!connectionString.Contains("charset", StringComparison.OrdinalIgnoreCase)
        && !connectionString.Contains("characterset", StringComparison.OrdinalIgnoreCase))
        builder.CharacterSet = "utf8mb4";
    return builder.ConnectionString;
}

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
