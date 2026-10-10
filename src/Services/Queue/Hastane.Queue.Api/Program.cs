using System.Text.Json.Serialization;
using Hastane.Queue.Api.Data;
using Hastane.Queue.Api.Hubs;
using Hastane.Queue.Api.Integration;
using Hastane.Queue.Api.Mapping;
using Hastane.Queue.Api.Services;
using Hastane.Queue.Api.Settings;
using Hastane.Shared.Security;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    // Enum JSON'da "Waiting" gibi metin olarak gelir/gider (sayı yerine).
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(QueueMappingConfig).Assembly);
builder.Services.AddMapster();

// DbContext Scoped (istek başına bir örnek). Bağlantı cümlesi user-secrets'tan; seçenek tembel okunur ki
// testte WebApplicationFactory'nin verdiği konteyner adresi geçerli olsun.
builder.Services.AddDbContext<QueueDbContext>((sp, options) =>
    options.UseSqlServer(sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")));

builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneAuthorization();
builder.Services.AddHastaneCurrentUser();

builder.Services.AddOptions<SchedulingOptions>()
    .BindConfiguration(SchedulingOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Check-in'de randevu sahipliği/durumu/günü Appointment servisinden doğrulanır; kullanıcının Authorization başlığı aynen iletilir.
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

// "Şimdi" tek yerden: testte sahte saat verilebilsin.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IQueueService, QueueService>();

// SignalR: hub anonim ve yalnız dinleme (bkz. QueueHub). Gelen iletiyi küçük tut: istemci zaten bir şey göndermemeli.
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 4 * 1024;
    o.EnableDetailedErrors = false;
});
builder.Services.AddSingleton<IQueueNotifier, QueueNotifier>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Yalnız geliştirmede: bekleyen migration'ları açılışta uygular.
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<QueueDbContext>().Database.Migrate();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<QueueHub>("/hubs/queue").AllowAnonymous();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
