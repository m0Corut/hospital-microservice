using System.Net;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Analytics.Tests;

// Yetki: tüm uçlar yalnız Admin. Kafka'ya ihtiyaç duymayan testler: tüketici KAPALI (boş BootstrapServers) kardeş ana bilgisayarı kullanılır.
[Collection(AnalyticsCollection.Name)]
public class AnalyticsAuthorizationTests : AnalyticsTestBase, IDisposable
{
    private readonly WebApplicationFactory<Program> _host;

    public AnalyticsAuthorizationTests(AnalyticsApiFactory factory) : base(factory) => _host = factory.WithKafka("");

    public void Dispose() => _host.Dispose();

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["GET api/analytics/doctors/top-viewed"] = "Policy:AdminOnly",
        ["GET api/analytics/search/top-terms"] = "Policy:AdminOnly",
        ["GET api/analytics/status"] = "Policy:AdminOnly",
        ["POST api/analytics/rebuild"] = "Policy:AdminOnly",
    };

    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(_host.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    // Golden yetki tablosu: bir politika sessizce değişir ya da yeni uç tabloya yazılmadan eklenirse kırılır.
    [Fact]
    public void EveryEndpoint_HasExactlyTheExpectedAuthorization()
    {
        var actual = new Dictionary<string, string>();
        foreach (var endpoint in _host.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is null) continue;
            var verbs = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var parts = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy is not null ? $"Policy:{a.Policy}" : $"Roles:{a.Roles}")
                .ToList();
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) parts = ["Anonymous"];
            foreach (var verb in verbs)
                actual[$"{verb} {endpoint.RoutePattern.RawText}"] = string.Join(" + ", parts);
        }

        Assert.Equal(Expected.OrderBy(k => k.Key).Select(k => $"{k.Key} => {k.Value}"),
            actual.OrderBy(k => k.Key).Select(k => $"{k.Key} => {k.Value}"));
    }

    public static TheoryData<string, string> Calls() => new()
    {
        { "GET", "/api/analytics/doctors/top-viewed" },
        { "GET", "/api/analytics/search/top-terms" },
        { "GET", "/api/analytics/status" },
        { "POST", "/api/analytics/rebuild" },
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Anonymous_is_401(string method, string url)
    {
        var response = await _host.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task Patient_and_doctor_are_403(string method, string url)
    {
        foreach (var role in new[] { HastaneRoles.Patient, HastaneRoles.Doctor })
        {
            var client = _host.CreateClient().WithBearer(TestJwt.Create("user-1", [role], role == HastaneRoles.Doctor ? "doc-1" : null));

            var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url), Ct);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Admin_passes_the_gate_on_every_read_endpoint()
    {
        var admin = AdminOf(_host);

        foreach (var url in new[] { "/api/analytics/doctors/top-viewed", "/api/analytics/search/top-terms", "/api/analytics/status" })
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url, Ct)).StatusCode);
    }
}
