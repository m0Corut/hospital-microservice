using Hastane.Notification.Api.Mapping;
using Hastane.Notification.Api.Services;
using Hastane.Notification.Api.Settings;
using Hastane.Shared.Security;
using Mapster;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

TypeAdapterConfig.GlobalSettings.Scan(typeof(NotificationMappingConfig).Assembly);
builder.Services.AddMapster();

builder.Services.Configure<DatabaseSettings>(builder.Configuration.GetSection("DatabaseSettings"));
builder.Services.AddOptions<SmtpOptions>()
    .BindConfiguration(SmtpOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.Configure<InternalOptions>(builder.Configuration.GetSection(InternalOptions.SectionName));

// MongoClient bağlantı havuzunu içerir, pahalıdır ve iş parçacığı güvenlidir: uygulama boyunca TEK örnek.
builder.Services.AddSingleton<IMongoClient>(sp =>
    new MongoClient(sp.GetRequiredService<IOptions<DatabaseSettings>>().Value.ConnectionString));
builder.Services.AddHostedService<MongoIndexInitializer>();

builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();

builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
builder.Services.AddHastaneAuthorization();
builder.Services.AddHastaneCurrentUser();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
