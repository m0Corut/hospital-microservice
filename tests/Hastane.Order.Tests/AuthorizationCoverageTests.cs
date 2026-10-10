using Hastane.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Order.Tests;

// Her uç bilerek korunmuş ya da bilerek anonim olmalı: yeni uç eklenip yetki etiketi unutulursa bu test kırılır.
[Collection(OrderCollection.Name)]
public class AuthorizationCoverageTests
{
    private readonly OrderApiFactory _factory;

    public AuthorizationCoverageTests(OrderApiFactory factory) => _factory = factory;

    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(_factory.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    // Yetki tablosu (golden) testi: her ucun beklenen yetkisi JWT-TASARIM.md §5 matrisiyle birebir burada yazili.
    // Bir politika sessizce gevserse ya da degisirse bu test kirilir.
    [Fact]
    public void EveryEndpoint_HasExactlyTheExpectedAuthorization()
    {
        var expected = new SortedDictionary<string, string>
        {
            ["POST api/orders/checkout"] = "Roles:Patient",
            ["GET api/orders"] = "Roles:Patient,Admin",
            ["GET api/orders/{id:guid}"] = "Roles:Patient,Admin",
            ["PATCH api/orders/{id:guid}/status"] = "Policy:AdminOnly",
            ["POST api/orders/{id:guid}/cancel"] = "Roles:Patient,Admin",
        };

        var actual = new SortedDictionary<string, string>();
        foreach (var endpoint in _factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
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
}
