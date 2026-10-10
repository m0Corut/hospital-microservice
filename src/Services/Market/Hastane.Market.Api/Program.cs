using System.Text.Json.Serialization;
using Hastane.Market.Api.Caching;
using Hastane.Market.Api.Data;
using Hastane.Market.Api.Messaging;
using Hastane.Market.Api.Mapping;
using Hastane.Market.Api.Services;
using Hastane.Market.Api.Settings;
using Hastane.Shared.Security;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    // Enum JSON'da "Vitamin" gibi METİN olarak gelir/gider; sayı ("1") kabul edilmez.
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(MarketMappingConfig).Assembly);
builder.Services.AddMapster();

builder.Services.AddOptions<MarketOptions>()
    .BindConfiguration(MarketOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.Configure<InternalOptions>(builder.Configuration.GetSection(InternalOptions.SectionName));

// Ürünler, stok ve rezervasyonlar SQL Server'da (transaction + koşullu UPDATE ile tutarlı stok).
// Bağlantı cümlesi user-secrets'tan; seçenek tembel okunur ki testte WebApplicationFactory'nin verdiği adres geçerli olsun.
builder.Services.AddDbContext<MarketDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")));

// Redis iki işte: ürün önbelleği (cache-aside) ve sepet. Tek bağlantı, tüm uygulama boyunca paylaşılır.
builder.Services.AddSingleton<IRedisGate, RedisGate>();
builder.Services.AddSingleton<IProductCache, ProductCache>();
builder.Services.AddSingleton<ICartStore, RedisCartStore>();

builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<IStockService, StockService>();

// Olay tüketicisi (RabbitMQ): OrderCancelled -> stok iadesi. RabbitMq:Host boşsa bellek içi taşıma.
builder.Services.AddMarketMessaging(builder.Configuration);

builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneAuthorization();
builder.Services.AddHastaneCurrentUser();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Yalnız geliştirmede: bekleyen migration'ları açılışta uygular (üretimde şemayı kontrollü bir dağıtım adımı değiştirmeli).
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MarketDbContext>();
    db.Database.Migrate();
    if (scope.ServiceProvider.GetRequiredService<IOptions<MarketOptions>>().Value.SeedDemoData)
        await DemoDataSeeder.SeedAsync(db);
}

// Sıra önemli: önce kimlik (token okunur), sonra yetki (politika uygulanır), en son uç.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
