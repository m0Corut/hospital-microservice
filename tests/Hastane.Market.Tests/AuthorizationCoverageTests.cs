using System.Net;
using System.Text;
using Hastane.Market.Api.Security;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Market.Tests;

// Her uç bilerek korunmuş ya da bilerek anonim olmalı: yeni uç eklenip yetki etiketi unutulursa bu test kırılır.
[Collection(MarketCollection.Name)]
public class AuthorizationCoverageTests(MarketApiFactory factory) : MarketTestBase(factory)
{
    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(Factory.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    // Yetki tablosu (golden): her ucun beklenen yetkisi burada birebir yazılı. Bir politika sessizce gevşerse ya da
    // değişirse bu test kırılır.
    [Fact]
    public void EveryEndpoint_HasExactlyTheExpectedAuthorization()
    {
        var expected = new SortedDictionary<string, string>
        {
            ["GET api/products"] = "Anonymous",
            ["GET api/products/{id:guid}"] = "Anonymous",
            ["POST api/products"] = "Policy:AdminOnly",
            ["PUT api/products/{id:guid}"] = "Policy:AdminOnly",
            ["DELETE api/products/{id:guid}"] = "Policy:AdminOnly",
            ["GET api/cart"] = "Roles:Patient",
            ["PUT api/cart/items/{productId:guid}"] = "Roles:Patient",
            ["DELETE api/cart"] = "Roles:Patient",
            ["POST api/internal/stock/reserve"] = "Anonymous",
            ["POST api/internal/stock/release"] = "Anonymous",
        };

        var actual = new SortedDictionary<string, string>();
        foreach (var endpoint in RouteEndpoints())
        {
            var verbs = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var parts = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => !string.IsNullOrEmpty(a.Policy) ? "Policy:" + a.Policy : "Roles:" + a.Roles).ToList();
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) parts.Add("Anonymous");
            actual[string.Join(",", verbs) + " " + endpoint.RoutePattern.RawText] = string.Join(" + ", parts);
        }

        Assert.Equal(expected, actual);
    }

    // "Anonim" iç uçlar yalnız X-Internal-Key filtresi sayesinde güvende: filtre her /api/internal ucunda bulunmalı.
    [Fact]
    public void EveryInternalEndpoint_CarriesTheInternalApiKeyFilter()
    {
        var internals = RouteEndpoints().Where(e => e.RoutePattern.RawText!.StartsWith("api/internal/", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(internals);
        Assert.All(internals, e => Assert.NotNull(e.Metadata.GetMetadata<InternalApiKeyAttribute>()));
    }

    private IEnumerable<RouteEndpoint> RouteEndpoints()
        => Factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null);

    // --- 401: korunan uçlar, yok/bozuk token ---

    public static TheoryData<string, string> ProtectedEndpoints() => new()
    {
        { "POST", "/api/products" },
        { "PUT", "/api/products/00000000-0000-0000-0000-000000000001" },
        { "DELETE", "/api/products/00000000-0000-0000-0000-000000000001" },
        { "GET", "/api/cart" },
        { "PUT", "/api/cart/items/00000000-0000-0000-0000-000000000001" },
        { "DELETE", "/api/cart" },
    };

    private static HttpRequestMessage Req(string method, string url) => new(new HttpMethod(method), url)
    {
        Content = new StringContent("{}", Encoding.UTF8, "application/json"),
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task NoToken_Returns401(string method, string url)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).SendAsync(Req(method, url), Ct)).StatusCode);

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
    public async Task UserWithoutRole_Gets403_OnWriteAndCart()
    {
        var client = ClientFor(TestJwt.Create(Unique("usr"), []));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Req("POST", "/api/products"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/cart", Ct)).StatusCode);
    }

    // Geçersiz token anonim uçta engel değil mi? Anonim uç token'a bakmaz ama bozuk token da 401 üretmemeli.
    [Fact]
    public async Task PublicEndpoints_IgnoreBrokenToken()
    {
        var client = ClientFor(TestJwt.CreateWithBrokenSignature("u1", [HastaneRoles.Admin]));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/products", Ct)).StatusCode);
    }
}
