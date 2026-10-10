using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Hastane.Shared.Hosting;

public static class DatabaseStartupExtensions
{
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    // Açılışta migration (ve örnek veri) çalışsın mı? Ayar açıkça verilmişse o geçerli; verilmemişse yalnız Development'ta.
    // Neden ayar: Docker ortamında (ASPNETCORE_ENVIRONMENT=Docker) şemayı kimse elle kurmaz, ama canlı sunucuda
    // varsayılan "kapalı" kalmalı: şema değişikliği kendiliğinden değil bilinçli yapılır.
    public static bool ShouldMigrateOnStartup(this WebApplication app) =>
        app.Configuration.GetValue<bool?>(MigrateOnStartupKey) ?? app.Environment.IsDevelopment();
}
