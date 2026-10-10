using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Identity.Tests;

// Yetki tablosu (golden) testi: her ucun beklenen yetkisi AÇIKÇA yazılı (JWT-TASARIM.md §5 + gece N3b uçları).
// Sınıf düzeyi + action düzeyi etiketler VE ile birleşir; bu yüzden parçalar sıralanıp "A + B" biçiminde karşılaştırılır.
public class IdentityAuthorizationTableTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;

    public IdentityAuthorizationTableTests(IdentityApiFactory factory) => _factory = factory;

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["POST api/auth/register"] = "Anonymous",
        ["POST api/auth/login"] = "Anonymous",
        ["GET api/auth/me"] = "Policy:AuthenticatedUser",
        ["PUT api/auth/me"] = "Policy:AuthenticatedUser",
        ["POST api/auth/change-password"] = "Policy:AuthenticatedUser",
        ["GET api/roles"] = "Policy:AdminOnly",
        ["POST api/roles"] = "Policy:AdminOnly",
        ["GET api/users/{id}"] = "Policy:AuthenticatedUser",
        ["GET api/users"] = "Policy:AuthenticatedUser + Policy:DoctorOrAdmin",
        ["POST api/users/{id}/roles"] = "Policy:AdminOnly + Policy:AuthenticatedUser",
        ["DELETE api/users/{id}/roles/{role}"] = "Policy:AdminOnly + Policy:AuthenticatedUser",
        ["PUT api/users/{id}/doctor-link"] = "Policy:AdminOnly + Policy:AuthenticatedUser",
        ["DELETE api/users/{id}/doctor-link"] = "Policy:AdminOnly + Policy:AuthenticatedUser",
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
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList();
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) parts = ["Anonymous"];
            foreach (var verb in verbs)
                actual[$"{verb} {endpoint.RoutePattern.RawText}"] = string.Join(" + ", parts);
        }

        Assert.Equal(Expected.OrderBy(k => k.Key).Select(k => $"{k.Key} => {k.Value}"),
            actual.OrderBy(k => k.Key).Select(k => $"{k.Key} => {k.Value}"));
    }
}
