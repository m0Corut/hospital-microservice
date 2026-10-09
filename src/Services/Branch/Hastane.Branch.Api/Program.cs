using Hastane.Branch.Api.Mapping;
using Hastane.Branch.Api.Services;
using Hastane.Branch.Api.Settings;
using Hastane.Shared.Security;
using Mapster;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Mapster: BranchMappingConfig gibi IRegister sınıfları derlemede taranır; IMapper DI'dan gelir.
TypeAdapterConfig.GlobalSettings.Scan(typeof(BranchMappingConfig).Assembly);
builder.Services.AddMapster();

// Ayarlar appsettings.json "DatabaseSettings" bölümünden (bağlantı cümlesi user-secrets'tan) okunur.
builder.Services.Configure<DatabaseSettings>(builder.Configuration.GetSection("DatabaseSettings"));
// MongoClient içinde bağlantı havuzu var; uygulama başına TEK örnek olmalı (Singleton), her istekte yenisi kurulmaz.
// Fabrika ilk kullanımda çalışır, bu yüzden testlerin ezdiği ayar da (konteyner adresi) buraya yansır.
builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(sp.GetRequiredService<IOptions<DatabaseSettings>>().Value.ConnectionString));
builder.Services.AddScoped<IBranchService, BranchService>();

// Gelen token'ı doğrulama (RS256, açık anahtar) + politikalar + "token'daki kullanıcı kim?" (ICurrentUser).
builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneAuthorization();
builder.Services.AddHastaneCurrentUser();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Sıra önemli: önce "kimsin" (token'ı çöz), sonra "iznin var mı"; ikisi de uç eşlemeden önce.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
