using Hastane.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Doctor.Tests;

// Yetki tablosu (golden) testi: her denetleyici ucunun beklenen yetkisi AÇIKÇA burada yazılı ve JWT-TASARIM.md §5
// matrisiyle birebir aynı. Bir politika sessizce değişirse (ör. AdminOnly -> AuthenticatedUser) ya da yeni uç
// eklenip tabloya yazılmazsa bu test kırılır.
public class DoctorAuthorizationTableTests : IClassFixture<DoctorApiFactory>
{
    private readonly DoctorApiFactory _factory;

    public DoctorAuthorizationTableTests(DoctorApiFactory factory) => _factory = factory;

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["GET api/doctors"] = "Anonymous",
        ["GET api/doctors/search"] = "Anonymous",
        ["GET api/doctors/batch"] = "Anonymous",
        ["GET api/doctors/{id}"] = "Policy:AuthenticatedUser",
        ["GET api/doctors/{id}/public"] = "Anonymous",
        ["POST api/doctors"] = "Policy:AdminOnly",
        ["PUT api/doctors/{id}"] = "Policy:DoctorOrAdmin",
        ["DELETE api/doctors/{id}"] = "Policy:AdminOnly",
    };

    [Fact]
    public void EveryEndpoint_HasExactlyTheExpectedAuthorization()
    {
        var actual = new Dictionary<string, string>();
        foreach (var endpoint in _factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>())
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
}
