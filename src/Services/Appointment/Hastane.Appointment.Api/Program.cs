using System.Text.Json.Serialization;
using Hastane.Appointment.Api.Data;
using Hastane.Appointment.Api.Integration;
using Hastane.Appointment.Api.Mapping;
using Hastane.Appointment.Api.Services;
using Hastane.Appointment.Api.Settings;
using Hastane.Shared.Security;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    // Enum JSON'da "Confirmed" gibi metin olarak gelir/gider (sayı yerine).
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(AppointmentMappingConfig).Assembly);
builder.Services.AddMapster();

// DbContext Scoped (istek başına bir örnek). Bağlantı cümlesi user-secrets'tan; seçenek tembel okunur ki
// testte WebApplicationFactory'nin verdiği konteyner adresi geçerli olsun.
builder.Services.AddDbContext<AppointmentDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")));

// Kimlik doğrulama (token geçerli mi?) + yetkilendirme (politikalar) + "token'daki kullanıcı kim?" (ICurrentUser).
builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneAuthorization();
builder.Services.AddHastaneCurrentUser();

// Doktorun haftalık programı Doctor servisinden (typed HttpClient). Gelen Authorization başlığı IHttpContextAccessor ile
// aynen iletilir; adres ve zaman aşımı "Services", saat dilimi "Scheduling" ayarından okunur.
builder.Services.AddOptions<DownstreamServicesOptions>()
    .BindConfiguration(DownstreamServicesOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<SchedulingOptions>()
    .BindConfiguration(SchedulingOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IDoctorScheduleLookup, DoctorScheduleLookup>((sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<DownstreamServicesOptions>>().Value;
    client.BaseAddress = new Uri(o.DoctorBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
});

builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IAppointmentDetailService, AppointmentDetailService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Yalnız geliştirmede: bekleyen migration'ları açılışta uygular (üretimde veritabanı şemasını
    // uygulama değil, kontrollü bir dağıtım adımı değiştirmeli).
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppointmentDbContext>().Database.Migrate();
}

// Sıra önemli: önce kimlik (token okunur), sonra yetki (politika uygulanır), en son uç.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
