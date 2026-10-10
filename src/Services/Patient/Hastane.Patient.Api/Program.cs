using System.Text.Json.Serialization;
using Hastane.Patient.Api.Data;
using Hastane.Patient.Api.Integration;
using Hastane.Patient.Api.Mapping;
using Hastane.Patient.Api.Services;
using Hastane.Patient.Api.Settings;
using Hastane.Shared.Security;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    // Enum JSON'da "APos" gibi metin olarak gelir/gider; sayı kabul edilmez (geçersiz sayı sessizce enum'a dönüşmesin).
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(PatientMappingConfig).Assembly);
builder.Services.AddMapster();

// Bağlantı cümlesi user-secrets'tan; seçenek tembel okunur ki testte WebApplicationFactory'nin verdiği konteyner adresi geçerli olsun.
builder.Services.AddDbContext<PatientDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")));

builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneAuthorization();
builder.Services.AddHastaneCurrentUser();

builder.Services.AddSingleton(TimeProvider.System);

// Servisten servise sorgular: doktorun hastayla randevusu var mı (Appointment) ve favori doktor var mı (Doctor).
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
builder.Services.AddHttpClient<IDoctorLookup, DoctorLookup>((sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value;
    client.BaseAddress = new Uri(o.DoctorBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
});

builder.Services.AddScoped<IPatientService, PatientService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Yalnız geliştirmede: bekleyen migration'ları açılışta uygular.
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PatientDbContext>().Database.Migrate();
}

// Sıra önemli: önce kimlik (token okunur), sonra yetki (politika uygulanır), en son uç.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
