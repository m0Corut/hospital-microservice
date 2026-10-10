using Hastane.Notification.Api.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Notification.Tests;

// Yetki tablosu (golden) + kapsam testi. İç uçlar "Anonymous + InternalKey": JWT istemezler ama X-Internal-Key kapısı şart.
// Yeni uç eklenip tabloya yazılmazsa ya da bir uç anahtarsız anonim kalırsa bu testler kırılır.
public class NotificationAuthorizationTableTests : IClassFixture<NotificationApiFactory>
{
    private readonly NotificationApiFactory _factory;

    public NotificationAuthorizationTableTests(NotificationApiFactory factory) => _factory = factory;

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["POST api/internal/notifications"] = "Anonymous + InternalKey",
        ["POST api/internal/notifications/email"] = "Anonymous + InternalKey",
        ["GET api/notifications/me"] = "Policy:AuthenticatedUser",
        ["POST api/notifications/{id}/read"] = "Policy:AuthenticatedUser",
        ["POST api/notifications/read-all"] = "Policy:AuthenticatedUser",
    };

    private IEnumerable<RouteEndpoint> ControllerEndpoints()
        => _factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null);

    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(_factory.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    [Fact]
    public void EveryEndpoint_HasExactlyTheExpectedAuthorization()
    {
        var actual = new Dictionary<string, string>();
        foreach (var endpoint in ControllerEndpoints())
        {
            var verbs = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var parts = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => !string.IsNullOrEmpty(a.Policy) ? $"Policy:{a.Policy}" : $"Roles:{a.Roles}")
                .ToList();
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) parts = ["Anonymous"];
            if (endpoint.Metadata.GetMetadata<InternalApiKeyAttribute>() is not null) parts.Add("InternalKey");
            foreach (var verb in verbs)
                actual[$"{verb} {endpoint.RoutePattern.RawText}"] = string.Join(" + ", parts);
        }

        Assert.Equal(Expected.OrderBy(k => k.Key).Select(k => $"{k.Key} => {k.Value}"),
            actual.OrderBy(k => k.Key).Select(k => $"{k.Key} => {k.Value}"));
    }

    [Fact]
    public void EveryAnonymousEndpoint_IsGuardedByInternalKey()
    {
        // Anahtarsız anonim uç = herkese açık uç. Anonim olan her uç iç anahtar kapısını da taşımalı.
        var unguarded = ControllerEndpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null
                        && e.Metadata.GetMetadata<InternalApiKeyAttribute>() is null)
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        Assert.True(unguarded.Count == 0, "InternalApiKey'siz anonim uçlar: " + string.Join("; ", unguarded));
    }

    [Fact]
    public void InternalEndpoints_LiveUnderApiInternalPrefix_SoGatewayCanLeaveThemOut()
    {
        var internalOnes = ControllerEndpoints()
            .Where(e => e.Metadata.GetMetadata<InternalApiKeyAttribute>() is not null)
            .Select(e => e.RoutePattern.RawText!)
            .ToList();

        Assert.NotEmpty(internalOnes);
        Assert.All(internalOnes, p => Assert.StartsWith("api/internal/", p));
    }
}
