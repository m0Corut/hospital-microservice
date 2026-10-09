using System.Net.Http.Headers;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.TestSupport;

public static class JwtTestAuthExtensions
{
    // İstemciye "Authorization: Bearer <token>" başlığı koyar. Aynı istemciyi döndürür (zincirlenir).
    public static HttpClient WithBearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Servisin token DOĞRULAMA anahtarını test açık anahtarıyla değiştirir (üretim jwt-public.pem devre dışı).
    // İki yoldan birden: yapılandırma (Jwt:PublicKeyPem) ve JwtBearerOptions son ayarı (PostConfigure).
    // Kullanım: factory'nin ConfigureWebHost'unda  builder.UseTestJwt();
    public static IWebHostBuilder UseTestJwt(this IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:PublicKeyPem"] = TestKeys.PublicKeyPem,
        }));
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.TokenValidationParameters.IssuerSigningKey = TestKeys.ValidationKey));
        return builder;
    }

    // Yalnız TOKEN ÜRETEN servis (Identity) için: imzalama özel anahtarını da test çiftiyle ezer.
    // UseTestJwt() ile birlikte kullanılırsa üretilen token'ı aynı servis doğrular.
    public static IWebHostBuilder UseTestJwtSigningKey(this IWebHostBuilder builder)
        => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:PrivateKeyPem"] = TestKeys.PrivateKeyPem,
        }));

    // Hazır bir factory'den test anahtarlı yeni factory türetir: factory.UseTestJwt().CreateClient()
    public static WebApplicationFactory<TEntryPoint> UseTestJwt<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory)
        where TEntryPoint : class
        => factory.WithWebHostBuilder(builder => builder.UseTestJwt());
}
