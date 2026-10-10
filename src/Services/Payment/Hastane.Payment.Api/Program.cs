using System.Text.Json.Serialization;
using Hastane.Payment.Api.Data;
using Hastane.Payment.Api.Integration;
using Hastane.Payment.Api.Mapping;
using Hastane.Payment.Api.Providers;
using Hastane.Payment.Api.Services;
using Hastane.Payment.Api.Settings;
using Hastane.Shared.Hosting;
using Hastane.Shared.Security;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

// Kimlik doğrulama (token geçerli mi?) + yetkilendirme (politikalar) + "token'daki kullanıcı kim?" (ICurrentUser).
builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneAuthorization();
builder.Services.AddHastaneCurrentUser();

// Servisten servise doğrulama (kart çekilmeden ÖNCE): randevu sahipliği/durumu ve doktor ücreti.
// Gelen Authorization başlığı IHttpContextAccessor ile aynen iletilir; adresler/zaman aşımı "Services" ayarından, tembel okunur.
builder.Services.AddOptions<DownstreamServicesOptions>()
    .BindConfiguration(DownstreamServicesOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IAppointmentLookup, AppointmentLookup>((sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value;
    client.BaseAddress = new Uri(o.AppointmentBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
});
builder.Services.AddHttpClient<IDoctorFeeLookup, DoctorFeeLookup>((sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value;
    client.BaseAddress = new Uri(o.DoctorBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
});

builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();

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
    scope.ServiceProvider.GetRequiredService<PaymentDbContext>().Database.Migrate();
}

// Sıra önemli: önce kimlik (token okunur), sonra yetki (politika uygulanır), en son uç.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
