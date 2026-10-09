using Hastane.Identity.Api.Constants;
using Microsoft.AspNetCore.Identity;

namespace Hastane.Identity.Api.Data;

// Referans veri: roller kod tarafından bilinir (Patient atanıyor), veritabanında da var olmalı.
// Idempotent: kaç kez çalışırsa çalışsın aynı sonuç, çift kayıt yok.
public static class RoleSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in AppRoles.All)
        {
            if (await roleManager.RoleExistsAsync(role)) continue;

            var result = await roleManager.CreateAsync(new IdentityRole(role));
            // Aynı anda başlayan iki örnek yarışırsa kaybeden hata alır; rol artık varsa sorun yok.
            if (!result.Succeeded && !await roleManager.RoleExistsAsync(role))
                throw new InvalidOperationException(
                    $"'{role}' rolü oluşturulamadı: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
