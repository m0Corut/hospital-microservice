using Hastane.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Payment.Tests;

// Her uç bilerek korunmuş ya da bilerek anonim olmalı: yeni uç eklenip yetki etiketi unutulursa bu test kırılır.
[Collection(PaymentCollection.Name)]
public class AuthorizationCoverageTests
{
    private readonly PaymentApiFactory _factory;

    public AuthorizationCoverageTests(PaymentApiFactory factory) => _factory = factory;

    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(_factory.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    // Yetki tablosu (golden) testi: her ucun beklenen yetkisi JWT-TASARIM.md §5 matrisiyle birebir burada yazili.
    // Sinif duzeyi [Authorize] ile metot duzeyi BIRLESIR (VE); bu yuzden her eylem kendi etiketini tasir ve
    // bir politika sessizce gevserse ya da degisirse bu test kirilir.
    [Fact]
    public void EveryEndpoint_HasExactlyTheExpectedAuthorization()
    {
        const string any = "Roles:Admin,Doctor,Patient";
        var expected = new SortedDictionary<string, string>
        {
            ["GET api/payments"] = any,
            ["GET api/payments/{id:guid}"] = any,
            ["POST api/payments"] = "Roles:Patient,Admin",
            ["POST api/payments/{id:guid}/refund"] = "Policy:AdminOnly",
            ["GET api/invoices"] = any,
            ["GET api/invoices/{id:guid}"] = any,
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
