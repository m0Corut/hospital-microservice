using Hastane.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Pharmacy.Tests;

// Her uç bilerek korunmuş ya da bilerek anonim olmalı: yeni uç eklenip yetki etiketi unutulursa bu test kırılır.
[Collection(PharmacyCollection.Name)]
public class AuthorizationCoverageTests
{
    private readonly PharmacyApiFactory _factory;

    public AuthorizationCoverageTests(PharmacyApiFactory factory) => _factory = factory;

    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(_factory.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    // Yetki tablosu (golden) testi: her ucun beklenen yetkisi burada yazılı. Bir politika sessizce gevşerse ya da
    // değişirse (ör. AdminOnly -> AuthenticatedUser) bu test kırılır.
    [Fact]
    public void EveryEndpoint_HasExactlyTheExpectedAuthorization()
    {
        const string admin = "Policy:AdminOnly";
        const string anyUser = "Policy:AuthenticatedUser";
        var expected = new SortedDictionary<string, string>
        {
            ["GET api/medicines"] = anyUser,
            ["GET api/medicines/low-stock"] = admin,
            ["GET api/medicines/{id:guid}"] = anyUser,
            ["POST api/medicines"] = admin,
            ["PUT api/medicines/{id:guid}"] = admin,
            ["DELETE api/medicines/{id:guid}"] = admin,
            ["POST api/medicines/{id:guid}/stock"] = admin,
            ["GET api/medicines/{id:guid}/movements"] = admin,
            ["POST api/pharmacy/dispense"] = admin,
            ["GET api/pharmacy/dispense/preview/{prescriptionId:guid}"] = admin,
            ["GET api/pharmacy/dispensations"] = admin,
            ["GET api/pharmacy/dispensations/by-prescription/{prescriptionId:guid}"] = admin,
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
