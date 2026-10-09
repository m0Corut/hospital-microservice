using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hastane.Identity.Tests;

// Özel anahtar yoksa uygulama veritabanına bile uzanmadan, anlaşılır bir hatayla durur.
// (Konteyner gerekmez: hata, bağlantıdan önce, servisler kurulurken fırlar.)
public class JwtStartupConfigTests
{
    private sealed class FactoryWithPrivateKey(string? pem) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
            => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // user-secrets'taki gerçek anahtar devreye girmesin diye açıkça ezilir.
                ["Jwt:PrivateKeyPem"] = pem ?? "",
                ["Seed:AdminEmail"] = "",
                ["Seed:AdminPassword"] = "",
            }));
    }

    private static string AllMessages(Exception? ex)
    {
        var parts = new List<string>();
        for (; ex is not null; ex = ex.InnerException) parts.Add(ex.Message);
        return string.Join(" | ", parts);
    }

    [Fact]
    public void MissingPrivateKey_StopsStartup_WithClearMessage()
    {
        using var factory = new FactoryWithPrivateKey(null);

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("Jwt:PrivateKeyPem yapılandırılmamış", AllMessages(ex));
    }

    [Fact]
    public void GarbagePrivateKey_StopsStartup_WithClearMessage()
    {
        using var factory = new FactoryWithPrivateKey("bu bir pem degil");

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("geçerli bir PEM özel anahtarı değil", AllMessages(ex));
    }
}
