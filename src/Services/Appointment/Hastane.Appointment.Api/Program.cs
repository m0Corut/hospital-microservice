using System.Text.Json.Serialization;
using Hastane.Appointment.Api.Data;
using Hastane.Appointment.Api.Mapping;
using Hastane.Appointment.Api.Services;
using Mapster;
using Microsoft.EntityFrameworkCore;

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

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
