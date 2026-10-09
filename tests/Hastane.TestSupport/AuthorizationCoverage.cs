using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.TestSupport;

// "Her denetleyici ucu bilerek korunmuş ya da bilerek anonim olmalı" kuralını otomatik denetler.
// Yeni bir uç eklenip [Authorize] / [AllowAnonymous] unutulursa bu test KIRILIR (sessizce herkese açık uç kalmaz).
// Kullanım (her servisin test projesinde):
//   var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(factory.Services);
//   Assert.Empty(missing);
public static class AuthorizationCoverage
{
    // Yetki bilgisi OLMAYAN denetleyici uçlarını "GET api/branches (BranchesController.GetAll)" biçiminde döndürür.
    public static IReadOnlyList<string> FindEndpointsWithoutAuthorizationMetadata(IServiceProvider services)
    {
        var endpoints = services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints);
        var missing = new List<string>();
        var scanned = 0;

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action is null) continue; // yalnız denetleyici uçları (OpenAPI belgesi gibi minimal uçlar kapsam dışı)
            scanned++;

            // Endpoint metadata'sı sınıf + metot düzeyindeki öznitelikleri birleştirilmiş taşır.
            var hasAuthorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
            var hasAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            if (hasAuthorize || hasAnonymous) continue;

            var verbs = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            missing.Add($"{string.Join(",", verbs)} {endpoint.RoutePattern.RawText} ({action.ControllerName}Controller.{action.ActionName})");
        }

        // Hiç uç taranmadıysa test sessizce geçmesin (yanlış kurulum: sahte yeşil).
        if (scanned == 0) missing.Add("(hiç denetleyici ucu bulunamadı: tarama çalışmıyor)");

        return missing;
    }
}
