using System.Text.Json.Serialization;
using Hastane.Payment.Api.Data;
using Hastane.Payment.Api.Mapping;
using Hastane.Payment.Api.Providers;
using Hastane.Payment.Api.Services;
using Hastane.Payment.Api.Settings;
using Mapster;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    // Enum JSON'da "Paid" gibi metin olarak gelir/gider (sayı yerine).
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(PaymentMappingConfig).Assembly);
builder.Services.AddMapster();

// Vergi oranı ve para birimi beyaz listesi appsettings "Billing" bölümünden; açılışta doğrulanır.
builder.Services.AddOptions<BillingOptions>()
    .BindConfiguration(BillingOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// DbContext Scoped (istek başına bir örnek). Bağlantı cümlesi user-secrets'tan; seçenek tembel okunur ki
// testte WebApplicationFactory'nin verdiği konteyner adresi geçerli olsun.
builder.Services.AddDbContext<PaymentDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")));

// Sağlayıcı arayüzün arkasında: gerçek sağlayıcıya geçmek için YALNIZ bu satır değişir.
builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();

builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Yalnız geliştirmede: bekleyen migration'ları açılışta uygular (üretimde veritabanı şemasını
    // uygulama değil, kontrollü bir dağıtım adımı değiştirmeli).
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.Migrate();
}

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
