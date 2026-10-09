using Hastane.Prescription.Api.Data;
using Hastane.Prescription.Api.Mapping;
using Hastane.Prescription.Api.Services;
using Mapster;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(PrescriptionMappingConfig).Assembly);
builder.Services.AddMapster();

// DbContext Scoped (istek başına bir örnek). Bağlantı cümlesi user-secrets'tan; seçenek tembel okunur ki
// testte WebApplicationFactory'nin verdiği konteyner adresi geçerli olsun.
builder.Services.AddDbContext<PrescriptionDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IPrescriptionService, PrescriptionService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Yalnız geliştirmede: bekleyen migration'ları açılışta uygular (üretimde veritabanı şemasını
    // uygulama değil, kontrollü bir dağıtım adımı değiştirmeli).
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PrescriptionDbContext>().Database.Migrate();
}

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
