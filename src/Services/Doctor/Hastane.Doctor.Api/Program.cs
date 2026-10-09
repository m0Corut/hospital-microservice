using System.Text.Json.Serialization;
using Hastane.Doctor.Api.Mapping;
using Hastane.Doctor.Api.Services;
using Hastane.Doctor.Api.Settings;
using Mapster;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// Enum'lar JSON'da metin ("Female", "Monday"); sayı kabul edilmez (anlamı belirsiz, sıra değişince bozulur).
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(DoctorMappingConfig).Assembly);
builder.Services.AddMapster();

// Ayarlar appsettings.json "DatabaseSettings" bölümünden (bağlantı cümlesi user-secrets'tan) okunur.
builder.Services.Configure<DatabaseSettings>(builder.Configuration.GetSection("DatabaseSettings"));

// MongoClient bağlantı havuzunu içerir, pahalıdır ve iş parçacığı güvenlidir: uygulama boyunca TEK örnek.
// İlk kullanımda kurulur; testte WebApplicationFactory bu kaydı konteyner adresiyle ezer.
builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(sp.GetRequiredService<IOptions<DatabaseSettings>>().Value.ConnectionString));

builder.Services.AddScoped<IDoctorService, DoctorService>();

// Açılışta dizinleri (BranchId, Gender, ClinicLocations.City) idempotent oluşturur.
builder.Services.AddHostedService<DoctorIndexInitializer>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
