using Hastane.Shared.Hosting;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hastane.Shared.Tests;

// Docker için eklenen iki küçük yapılandırma özelliği: PEM'i dosyadan okuma ve açılışta migration ayarı.
public sealed class DockerConfigurationTests
{
    [Fact]
    public void PemFiles_AreReadWhenPemValuesAreEmpty()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var privatePath = Path.Combine(dir.FullName, "private.pem");
            var publicPath = Path.Combine(dir.FullName, "public.pem");
            File.WriteAllText(privatePath, TestKeys.PrivateKeyPem);
            File.WriteAllText(publicPath, TestKeys.PublicKeyPem);

            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:PrivateKeyPemFile"] = privatePath,
                ["Jwt:PublicKeyPemFile"] = publicPath,
            }).Build();
            var services = new ServiceCollection().AddHastaneJwtAuthentication(config);
            using var provider = services.BuildServiceProvider();

            var jwt = provider.GetRequiredService<IOptions<JwtOptions>>().Value;

            Assert.Equal(TestKeys.PrivateKeyPem, jwt.PrivateKeyPem);
            Assert.Equal(TestKeys.PublicKeyPem, jwt.PublicKeyPem);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void PemValue_WinsOverFile()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:PublicKeyPem"] = TestKeys.PublicKeyPem,
            ["Jwt:PublicKeyPemFile"] = "/yok/olmayan/dosya.pem",
        }).Build();
        using var provider = new ServiceCollection().AddHastaneJwtAuthentication(config).BuildServiceProvider();

        Assert.Equal(TestKeys.PublicKeyPem, provider.GetRequiredService<IOptions<JwtOptions>>().Value.PublicKeyPem);
    }

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Production", null, false)]
    [InlineData("Docker", null, false)]
    [InlineData("Docker", "true", true)]
    [InlineData("Development", "false", false)]
    public void MigrateOnStartup_DefaultsToDevelopmentOnly_AndCanBeOverridden(string environment, string? setting, bool expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        if (setting is not null) builder.Configuration["Database:MigrateOnStartup"] = setting;
        using var app = builder.Build();

        Assert.Equal(expected, app.ShouldMigrateOnStartup());
    }
}