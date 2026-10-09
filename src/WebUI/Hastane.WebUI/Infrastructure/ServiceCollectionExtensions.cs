using Hastane.Shared.Security;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Hastane.WebUI.Infrastructure;

public static class ServiceCollectionExtensions
{
    // IGatewayClient + bearer ekleyen handler + alan istemcilerinin otomatik kaydı.
    public static IServiceCollection AddWebUiGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GatewayOptions>().Bind(configuration.GetSection(GatewayOptions.SectionName));
        services.AddHttpContextAccessor();
        services.AddTransient<BearerTokenHandler>();

        services.AddHttpClient<IGatewayClient, GatewayClient>((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
                http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            .AddHttpMessageHandler<BearerTokenHandler>();

        services.AddApiClients();
        return services;
    }

    // Hastane.WebUI.Clients içindeki "XxxClient" sınıflarını "IXxxClient" arayüzüyle kaydeder.
    // Yeni ekran eklerken Program.cs'e dokunulmaz: dosyayı Clients/ altına koyması yeter.
    private static void AddApiClients(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            if (!type.IsClass || type.IsAbstract || type.Namespace != "Hastane.WebUI.Clients" || !type.Name.EndsWith("Client"))
                continue;

            var contract = type.GetInterface("I" + type.Name);
            if (contract is not null)
                services.AddScoped(contract, type);
        }
    }

    // Çerez oturumu + JWT doğrulama parametreleri (Shared). JwtBearer şeması yalnız doğrulama ayarı taşır;
    // varsayılan şema ÇEREZ olduğu için WebUI'a gelen isteklerde Authorization başlığı aranmaz.
    public static IServiceCollection AddWebUiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHastaneJwtAuthentication(configuration);

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = AuthConstants.CookieScheme;
                options.DefaultChallengeScheme = AuthConstants.CookieScheme;
            })
            .AddCookie(AuthConstants.CookieScheme, options =>
            {
                options.Cookie.Name = AuthConstants.CookieName;
                options.Cookie.HttpOnly = true;                                  // JavaScript okuyamaz (XSS ile çalınmaz)
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;  // yerelde http, canlıda https
                options.Cookie.SameSite = SameSiteMode.Lax;                      // başka sitenin POST'u çerezi taşımaz
                options.SlidingExpiration = false;                               // süre = token ömrü, uzamaz
                options.LoginPath = "/Account/Login";
                options.ReturnUrlParameter = "returnUrl";
                // Rol yetmiyorsa girişe değil 403'e: hata sayfası "Yetkiniz yok" der.
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddSingleton<IAccessTokenValidator, AccessTokenValidator>();
        return services;
    }

    // Anahtarlar diskte kalıcı: yeniden başlatınca çerezler/antiforgery belirteçleri geçersiz olmaz.
    public static IServiceCollection AddWebUiDataProtection(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var builder = services.AddDataProtection().SetApplicationName("Hastane.WebUI");

        var path = configuration["DataProtection:KeyPath"];
        if (string.IsNullOrWhiteSpace(path) && environment.IsDevelopment())
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hastane-webui", "keys");

        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(path);
            builder.PersistKeysToFileSystem(new DirectoryInfo(path));
            if (OperatingSystem.IsWindows()) builder.ProtectKeysWithDpapi(); // anahtar dosyası düz metin kalmasın
        }

        return services;
    }
}
