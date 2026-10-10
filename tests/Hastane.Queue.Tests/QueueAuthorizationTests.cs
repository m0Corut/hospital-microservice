using System.Net;
using System.Text;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Queue.Tests;

[Collection(QueueCollection.Name)]
public class QueueAuthorizationTests : QueueTestBase
{
    private const string Guid1 = "00000000-0000-0000-0000-000000000001";

    public QueueAuthorizationTests(QueueApiFactory factory) : base(factory)
    {
    }

    // Her uç bilerek korunmuş ya da bilerek anonim olmalı: yeni uç eklenip yetki etiketi unutulursa bu test kırılır.
    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(Factory.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    // Yetki tablosu (golden): her ucun beklenen yetkisi JWT-TASARIM.md §5 matrisiyle birebir burada yazılı.
    [Fact]
    public void EveryEndpoint_HasExactlyTheExpectedAuthorization()
    {
        var expected = new SortedDictionary<string, string>
        {
            ["POST api/queue/check-in"] = "Policy:PatientOnly",
            ["GET api/queue/me/today"] = "Policy:PatientOnly",
            ["GET api/queue/doctor/today"] = "Policy:DoctorOrAdmin",
            ["POST api/queue/doctor/call-next"] = "Policy:DoctorOnly",
            ["POST api/queue/{id:guid}/call"] = "Policy:DoctorOrAdmin",
            ["POST api/queue/{id:guid}/complete"] = "Policy:DoctorOrAdmin",
            ["POST api/queue/{id:guid}/no-show"] = "Policy:DoctorOrAdmin",
            ["GET api/queue/display"] = "Anonymous",
        };

        var actual = new SortedDictionary<string, string>();
        foreach (var endpoint in Factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is null) continue;
            var verbs = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var parts = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => !string.IsNullOrEmpty(a.Policy) ? "Policy:" + a.Policy : "Roles:" + a.Roles).ToList();
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) parts.Add("Anonymous");
            actual[string.Join(",", verbs) + " " + endpoint.RoutePattern.RawText] = string.Join(" + ", parts);
        }

        Assert.Equal(expected, actual);
    }

    // Hub anonim olmalı (kasıtlı): hub ucu AllowAnonymous metadatası taşır.
    [Fact]
    public void Hub_IsExplicitlyAnonymous()
    {
        var hub = Factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/hubs/queue");

        Assert.NotNull(hub.Metadata.GetMetadata<IAllowAnonymous>());
    }

    private static readonly (string Method, string Url)[] Protected =
    [
        ("POST", "/api/queue/check-in"),
        ("GET", "/api/queue/me/today"),
        ("GET", "/api/queue/doctor/today"),
        ("POST", "/api/queue/doctor/call-next"),
        ("POST", $"/api/queue/{Guid1}/call"),
        ("POST", $"/api/queue/{Guid1}/complete"),
        ("POST", $"/api/queue/{Guid1}/no-show"),
    ];

    public static TheoryData<string, string> ProtectedEndpoints()
    {
        var data = new TheoryData<string, string>();
        foreach (var (method, url) in Protected) data.Add(method, url);
        return data;
    }

    private static HttpRequestMessage Req(string method, string url) => new(new HttpMethod(method), url)
    {
        Content = new StringContent("{}", Encoding.UTF8, "application/json"),
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task NoToken_Returns401(string method, string url)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor().SendAsync(Req(method, url), Ct)).StatusCode);

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task InvalidTokens_Return401(string method, string url)
    {
        var tokens = new[]
        {
            TestJwt.CreateExpired("u1", [HastaneRoles.Admin]),
            TestJwt.CreateWithBrokenSignature("u1", [HastaneRoles.Admin]),
            TestJwt.CreateAlgNone("u1", [HastaneRoles.Admin]),
            TestJwt.CreateHs256WithPublicKeyAsSecret("u1", [HastaneRoles.Admin]),
        };
        foreach (var token in tokens)
            Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(token).SendAsync(Req(method, url), Ct)).StatusCode);
    }

    [Fact]
    public async Task UserWithoutRole_Gets403_OnEveryProtectedEndpoint()
    {
        var client = ClientFor(TestJwt.Create(Unique("usr"), []));
        foreach (var (method, url) in Protected)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Req(method, url), Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task CheckIn_DoctorAndAdmin_Get403()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(DoctorToken(Unique("doc"))).SendAsync(Req("POST", "/api/queue/check-in"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(AdminToken()).SendAsync(Req("POST", "/api/queue/check-in"), Ct)).StatusCode);
    }

    [Fact]
    public async Task MeToday_Doctor_Gets403()
        => Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(DoctorToken(Unique("doc"))).GetAsync("/api/queue/me/today", Ct)).StatusCode);

    [Fact]
    public async Task Display_NeedsNoToken()
        => Assert.Equal(HttpStatusCode.OK, (await ClientFor().GetAsync($"/api/queue/display?doctorId={Unique("doc")}", Ct)).StatusCode);

    [Fact]
    public async Task MeToday_PatientSeesNothingOfOthers()
    {
        var doctor = Unique("doc");
        await NewEntryAsync(doctor);

        var list = await ReadAsync<List<Hastane.Queue.Api.Dtos.MyQueueEntryDto>>(
            await ClientFor(PatientToken(Unique("pat"))).GetAsync("/api/queue/me/today", Ct));

        Assert.Empty(list);
    }

    [Fact]
    public async Task NonGuidIdRoute_Returns404_NotServerError()
        => Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(AdminToken()).PostAsync("/api/queue/abc/call", null, Ct)).StatusCode);
}
