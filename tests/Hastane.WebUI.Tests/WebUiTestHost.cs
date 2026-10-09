using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Hastane.TestSupport;
using Hastane.WebUI.Infrastructure.Gateway;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hastane.WebUI.Tests;

public sealed record RecordedRequest(string Method, string Path, string Authorization, string Body);

/// <summary>Gerçek Kestrel'de rastgele portta sahte gateway. Yol + yöntem başına yanıt tanımlanır, istekler kaydedilir.</summary>
public sealed class FakeGateway : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, Func<HttpContext, Task>> _routes = new();

    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public string Url { get; }

    private FakeGateway(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    public static async Task<FakeGateway> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        FakeGateway? self = null;
        app.Run(async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            self!.Requests.Enqueue(new RecordedRequest(ctx.Request.Method, ctx.Request.Path.Value ?? "",
                ctx.Request.Headers.Authorization.ToString(), body));

            if (self._routes.TryGetValue($"{ctx.Request.Method} {ctx.Request.Path.Value}", out var handler))
                await handler(ctx);
            else
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        self = new FakeGateway(app, address);
        return self;
    }

    public void Map(string method, string path, Func<HttpContext, Task> handler) => _routes[$"{method} {path}"] = handler;

    public void MapJson(string method, string path, int status, object body)
        => Map(method, path, ctx =>
        {
            ctx.Response.StatusCode = status;
            return ctx.Response.WriteAsJsonAsync(body);
        });

    public void MapStatus(string method, string path, int status)
        => Map(method, path, ctx =>
        {
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        });

    public void MapLogin(string token, string firstName = "Ayşe", string lastName = "Yılmaz", params string[] roles)
        => MapJson("POST", "/gateway/auth/login", 200, new
        {
            accessToken = token,
            tokenType = "Bearer",
            expiresAt = DateTime.UtcNow.AddMinutes(30),
            user = new { id = "user-1", firstName, lastName, email = "ayse@test.local", roles },
        });

    public void MapBranches(params string[] names)
        => MapJson("GET", "/gateway/branches", 200, names.Select((n, i) => new { id = $"b{i}", name = n, description = $"{n} açıklaması", icon = (string?)null }));

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>Yalnız testlerde var olan denetleyici: ekran yazan kod yerine gateway hata yollarını sınar.</summary>
[Route("probe")]
public class ProbeController(IGatewayClient gateway) : Controller
{
    // Gateway'e giden isteğin cevabı verilen kodla döner; GatewayExceptionFilter davranışını görmek için.
    [HttpGet("call/{status:int}"), Authorize]
    public async Task<IActionResult> Call(int status, CancellationToken ct)
    {
        await gateway.GetAsync<object>($"/gateway/probe/{status}", ct: ct);
        return Content("tamam");
    }

    [HttpGet("boom"), AllowAnonymous]
    public IActionResult Boom() => throw new InvalidOperationException("GIZLI-YIGIN-IZI");

    [HttpGet("whoami"), Authorize]
    public IActionResult WhoAmI() => Content(User.Identity?.Name ?? "?");
}

public sealed class WebUiFactory(string gatewayUrl, bool withProbe) : WebApplicationFactory<Program>
{
    private readonly string _keyPath = Path.Combine(Path.GetTempPath(), "hastane-webui-test-keys-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestJwt(); // doğrulama anahtarı: test açık anahtarı (üretim anahtarı değil)
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gateway:BaseUrl"] = gatewayUrl,
            ["DataProtection:KeyPath"] = _keyPath,
        }));

        if (withProbe)
            builder.ConfigureServices(services => services.AddControllersWithViews().AddApplicationPart(typeof(ProbeController).Assembly));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (Directory.Exists(_keyPath)) Directory.Delete(_keyPath, true); } catch (IOException) { }
    }
}

public sealed partial class WebUiTestHost : IAsyncDisposable
{
    public FakeGateway Gateway { get; }
    public WebUiFactory Factory { get; }

    private WebUiTestHost(FakeGateway gateway, WebUiFactory factory)
    {
        Gateway = gateway;
        Factory = factory;
    }

    public static async Task<WebUiTestHost> StartAsync(bool withProbe = true)
    {
        var gateway = await FakeGateway.StartAsync();
        return new WebUiTestHost(gateway, new WebUiFactory(gateway.Url, withProbe));
    }

    /// <summary>Gateway adresi kapalı bir porta işaret eder: "servis kapalı" senaryosu.</summary>
    public static WebUiTestHost StartWithDeadGateway()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return new WebUiTestHost(null!, new WebUiFactory($"http://127.0.0.1:{port}", withProbe: true));
    }

    public HttpClient CreateClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public static string NewToken(string role, string name = "Ayşe Yılmaz")
        => TestJwt.Create("user-1", [role], name: name);

    /// <summary>Gateway'in login yanıtını ayarlar, formu antiforgery ile gönderir; yanıtı döndürür.</summary>
    public async Task<HttpResponseMessage> LoginAsync(HttpClient client, string token, string? returnUrl = null, string[]? roles = null)
    {
        Gateway.MapLogin(token, roles: roles ?? ["Patient"]);
        return await PostLoginFormAsync(client, "ayse@test.local", "Sifre123", returnUrl);
    }

    public static async Task<HttpResponseMessage> PostLoginFormAsync(HttpClient client, string email, string password, string? returnUrl = null)
    {
        var page = await client.GetAsync("/Account/Login");
        var html = await page.Content.ReadAsStringAsync();
        var form = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(html),
        };
        if (returnUrl is not null) form["ReturnUrl"] = returnUrl;
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));
    }

    public void MapProblem(string method, string path, int status, string detail)
        => Gateway.MapJson(method, path, status, new { status, title = "Hata", detail });

    public void MapJson201(string path)
        => Gateway.MapJson("POST", path, 201, new { id = "user-1", firstName = "Ayşe", lastName = "Yılmaz", email = "ayse@test.local", roles = new[] { "Patient" } });

    /// <summary>Sayfayı GET edip antiforgery belirtecini alır, alanlarla birlikte POST eder.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        var html = await (await client.GetAsync(path)).Content.ReadAsStringAsync();
        fields["__RequestVerificationToken"] = ExtractAntiforgeryToken(html);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    public static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryRegex().Match(html);
        Assert.True(match.Success, "Sayfada antiforgery belirteci yok.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryRegex();

    public static string? AuthSetCookie(HttpResponseMessage response)
        => response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(".Hastane.Auth", StringComparison.Ordinal))
            : null;

    public async ValueTask DisposeAsync()
    {
        Factory.Dispose();
        if (Gateway is not null) await Gateway.DisposeAsync();
    }
}

public static class HtmlExtensions
{
    // Razor, modelden gelen Türkçe karakterleri HTML varlığına çevirir (ş → &#x15F;); karşılaştırmadan önce çözülür.
    public static async Task<string> ReadHtmlAsync(this HttpContent content)
        => WebUtility.HtmlDecode(await content.ReadAsStringAsync());
}
