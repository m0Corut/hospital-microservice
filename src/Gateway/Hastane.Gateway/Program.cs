using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Rotalar kodda değil dosyada: yeni servis eklemek = ocelot.json'a satır eklemek.
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);
builder.Services.AddOcelot(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

// Ocelot "son durak" middleware'idir (istek ona girince başka uca düşmez); /health'i
// ona göndermemek için yalnız diğer yollarda devreye sokulur.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/health"),
    branch =>
    {
        // SignalR (kuyruk ekranı) WebSocket ile gelir: Ocelot WebSocket vekili için UseWebSockets Ocelot'tan ÖNCE gelmeli
        // (ocelot.json'daki "ws" şemalı rota: /gateway/hubs/queue). İstemci skipNegotiation + WebSockets ile bağlanır.
        branch.UseWebSockets();
        branch.UseOcelot().GetAwaiter().GetResult();
    });

app.Run();

// Test projesinin WebApplicationFactory<Program> kullanabilmesi için.
public partial class Program { }
