using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hastane.Gateway.Tests;

/// <summary>Yansıtıcı sahte servis: aldığı yol, sorgu, yöntem, Authorization ve gövdeyi JSON olarak geri verir.
/// Durum kodu X-Fake-Status başlığıyla seçilir (Ocelot başlıkları aynen iletir).</summary>
public sealed class FakeDownstream : IAsyncDisposable
{
    private readonly WebApplication _app;

    public int Port { get; }

    private FakeDownstream(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    public static async Task<FakeDownstream> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        app.UseWebSockets();
        app.Run(async ctx =>
        {
            // WebSocket (SignalR rotası): gelen ilk metni yol + sorgu ile birlikte geri yansıtır, sonra kapanır.
            if (ctx.WebSockets.IsWebSocketRequest)
            {
                using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
                var buffer = new byte[1024];
                var received = await socket.ReceiveAsync(buffer, ctx.RequestAborted);
                var text = System.Text.Encoding.UTF8.GetString(buffer, 0, received.Count);
                var reply = System.Text.Encoding.UTF8.GetBytes($"echo:{text}|{ctx.Request.Path.Value}{ctx.Request.QueryString.Value}");
                await socket.SendAsync(reply, System.Net.WebSockets.WebSocketMessageType.Text, true, ctx.RequestAborted);
                await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bitti", ctx.RequestAborted);
                return;
            }

            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = int.TryParse(ctx.Request.Headers["X-Fake-Status"], out var s) ? s : 200;
            await ctx.Response.WriteAsJsonAsync(new Echo(
                ctx.Request.Method,
                ctx.Request.Path.Value ?? "",
                ctx.Request.QueryString.Value ?? "",
                ctx.Request.Headers.Authorization.ToString(),
                body));
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new FakeDownstream(app, new Uri(address).Port);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed record Echo(string Method, string Path, string Query, string Authorization, string Body);

/// <summary>Gerçek Gateway; ocelot.json'a dokunmadan her rotanın host/port değerini test düzeyinde ezer.</summary>
public sealed class GatewayFactory(int downstreamPort) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var overrides = new Dictionary<string, string?>();
        for (var i = 0; i < OcelotRoutes.Read().Count; i++)
        {
            overrides[$"Routes:{i}:DownstreamHostAndPorts:0:Host"] = "127.0.0.1";
            overrides[$"Routes:{i}:DownstreamHostAndPorts:0:Port"] = downstreamPort.ToString();
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(overrides));
    }
}

public sealed record OcelotRoute(string Upstream, string Downstream, string[] Methods);

public static class OcelotRoutes
{
    public static IReadOnlyList<OcelotRoute> Read()
    {
        // ocelot.json, Gateway projesinden test çıktısına kopyalanır.
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ocelot.json")));
        return doc.RootElement.GetProperty("Routes").EnumerateArray()
            .Select(r => new OcelotRoute(
                r.GetProperty("UpstreamPathTemplate").GetString()!,
                r.GetProperty("DownstreamPathTemplate").GetString()!,
                r.GetProperty("UpstreamHttpMethod").EnumerateArray().Select(m => m.GetString()!).ToArray()))
            .ToList();
    }

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
