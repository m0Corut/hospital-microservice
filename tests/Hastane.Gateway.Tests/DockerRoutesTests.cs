using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Hastane.Gateway.Tests;

/// <summary>ocelot.json (geliştirme: localhost) ile ocelot.Docker.json (konteyner adları) ayrışmamalı.
/// İkincisini scripts/sync-ocelot-docker.ps1 üretir; bu testler elle düzenlenip sapmasını yakalar.</summary>
public sealed class DockerRoutesTests
{
    // Geliştirme portu -> docker-compose.full.yml servis adı.
    private static readonly Dictionary<int, string> ServiceByDevPort = new()
    {
        [5001] = "identity",
        [5101] = "branch",
        [5102] = "doctor",
        [5103] = "review",
        [5104] = "appointment",
        [5105] = "prescription",
        [5106] = "patient",
        [5107] = "queue",
        [5108] = "pharmacy",
        [5109] = "market",
        [5110] = "order",
        [5111] = "payment",
        [5112] = "notification",
        [5113] = "medicalrecord",
        [5114] = "chat",
        [5115] = "analytics",
    };

    private static JsonElement[] Routes(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)));
        return doc.RootElement.GetProperty("Routes").EnumerateArray().Select(r => r.Clone()).ToArray();
    }

    private static (string Host, int Port) Target(JsonElement route)
    {
        var hp = route.GetProperty("DownstreamHostAndPorts").EnumerateArray().Single();
        return (hp.GetProperty("Host").GetString()!, hp.GetProperty("Port").GetInt32());
    }

    [Fact]
    public void Docker_file_has_same_routes_in_same_order_as_ocelot_json()
    {
        var dev = Routes("ocelot.json");
        var docker = Routes("ocelot.Docker.json");

        Assert.Equal(dev.Length, docker.Length);
        for (var i = 0; i < dev.Length; i++)
        {
            foreach (var key in new[] { "UpstreamPathTemplate", "DownstreamPathTemplate", "DownstreamScheme" })
                Assert.Equal(dev[i].GetProperty(key).GetString(), docker[i].GetProperty(key).GetString());

            Assert.Equal(
                dev[i].GetProperty("UpstreamHttpMethod").EnumerateArray().Select(m => m.GetString()),
                docker[i].GetProperty("UpstreamHttpMethod").EnumerateArray().Select(m => m.GetString()));
        }
    }

    [Fact]
    public void Docker_file_points_every_route_at_the_container_name_of_the_dev_port_on_8080()
    {
        var dev = Routes("ocelot.json");
        var docker = Routes("ocelot.Docker.json");

        for (var i = 0; i < dev.Length; i++)
        {
            var (_, devPort) = Target(dev[i]);
            var (host, port) = Target(docker[i]);

            Assert.True(ServiceByDevPort.TryGetValue(devPort, out var expectedHost), $"Tabloda olmayan geliştirme portu: {devPort}");
            Assert.Equal(expectedHost, host);
            Assert.Equal(8080, port);
            Assert.NotEqual("localhost", host);
        }
    }

    [Fact]
    public void Gateway_loads_ocelot_Docker_json_when_environment_is_Docker()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Docker"));
        var config = factory.Services.GetRequiredService<IConfiguration>();

        Assert.Equal("identity", config["Routes:0:DownstreamHostAndPorts:0:Host"]);
        Assert.Equal("8080", config["Routes:0:DownstreamHostAndPorts:0:Port"]);
    }

    [Fact]
    public void Gateway_keeps_localhost_routes_outside_Docker_environment()
    {
        using var factory = new WebApplicationFactory<Program>();
        var config = factory.Services.GetRequiredService<IConfiguration>();

        Assert.Equal("localhost", config["Routes:0:DownstreamHostAndPorts:0:Host"]);
        Assert.Equal("5001", config["Routes:0:DownstreamHostAndPorts:0:Port"]);
    }
}