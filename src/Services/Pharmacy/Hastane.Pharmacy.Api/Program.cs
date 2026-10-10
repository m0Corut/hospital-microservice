using System.Text.Json.Serialization;
using Hastane.Pharmacy.Api.Behaviors;
using Hastane.Pharmacy.Api.Data;
using Hastane.Pharmacy.Api.Features.Dispensing;
using Hastane.Pharmacy.Api.Features.Medicines;
using Hastane.Pharmacy.Api.Integration;
using Hastane.Pharmacy.Api.Settings;
using Hastane.Pharmacy.Api.Validation;
using Hastane.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    // Enum JSON'da "Tablet" gibi metin olarak gelir/gider (sayı yerine).
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// DbContext Scoped (istek başına bir örnek). Bağlantı cümlesi user-secrets'tan; seçenek tembel okunur ki
// testte WebApplicationFactory'nin verdiği konteyner adresi geçerli olsun.
builder.Services.AddDbContext<PharmacyDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")));

builder.Services.AddSingleton(TimeProvider.System);

// CQRS + MediatR. Handler'lar derlemeden taranıp bulunur. Pipeline sırası = kayıt sırası, İLK KAYIT EN DIŞTA:
//   istek -> LoggingBehavior -> ValidationBehavior -> Handler -> (sonuç aynı yoldan geri)
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// Geçersiz istek ValidationBehavior'dan istisna olarak çıkar; burada 400 + alan hatalarına çevrilir.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();

// Handler'ların ortak yardımcıları (stok değiştirme tek yol, karşılama planı).
builder.Services.AddScoped<StockLedger>();
builder.Services.AddScoped<DispensePlanner>();

// Kimlik doğrulama (token geçerli mi?) + yetkilendirme (politikalar) + "token'daki kullanıcı kim?" (ICurrentUser).
builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneAuthorization();
builder.Services.AddHastaneCurrentUser();

// Reçete okuma (karşılama anında): Authorization başlığı IHttpContextAccessor ile aynen iletilir; adres/zaman aşımı "Services" ayarından.
builder.Services.AddOptions<DownstreamServicesOptions>()
    .BindConfiguration(DownstreamServicesOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IPrescriptionLookup, PrescriptionLookup>((sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value;
    client.BaseAddress = new Uri(o.PrescriptionBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
});

var app = builder.Build();

// Yakalanmamış istisnalar (ValidationExceptionHandler dahil) bu ara katmandan geçer.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Yalnız geliştirmede: bekleyen migration'ları açılışta uygular (üretimde şemayı uygulama değil, kontrollü bir dağıtım adımı değiştirmeli).
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PharmacyDbContext>().Database.Migrate();
}

// Sıra önemli: önce kimlik (token okunur), sonra yetki (politika uygulanır), en son uç.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
