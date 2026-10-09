using Hastane.Identity.Api.Entities;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Identity;

namespace Hastane.Identity.Api.Data;

// İlk Admin (bootstrap): Admin'i atayacak bir Admin yoksa sistem kilitli doğar. Bu yüzden ilk Admin açılışta,
// Seed:AdminEmail / Seed:AdminPassword (user-secrets ya da ortam değişkeni; depoda YOK) verilmişse oluşturulur.
public static class AdminSeeder
{
    public static Task SeedAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        return SeedAsync(services, config["Seed:AdminEmail"], config["Seed:AdminPassword"]);
    }

    public static async Task SeedAsync(IServiceProvider services, string? email, string? password)
    {
        // Değerlerden biri yoksa sessizce atla (normal durum: Admin zaten var, değerler artık verilmiyor).
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        email = email.Trim();

        using var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSeeder");

        // Yalnız HİÇ Admin yoksa: sonradan eklenen/silinen Admin'leri açılışta geri getirmeyiz.
        if ((await userManager.GetUsersInRoleAsync(HastaneRoles.Admin)).Count > 0) return;

        // Aynı e-postayla önceden kayıt olmuş (Patient) biri varsa ONU Admin YAPMAYIZ: register herkese açık,
        // saldırgan beklenen admin e-postasıyla önceden kayıt olup yükselmeyi kapabilirdi.
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            logger.LogWarning("Seed:AdminEmail ile kayıtlı bir kullanıcı zaten var; Admin oluşturulmadı.");
            return;
        }

        var admin = new AppUser
        {
            UserName = email,
            Email = email,
            FirstName = "Sistem",
            LastName = "Yöneticisi",
            CreatedAt = DateTime.UtcNow,
        };
        var created = await userManager.CreateAsync(admin, password);
        if (!created.Succeeded)
            throw new InvalidOperationException(
                $"İlk Admin oluşturulamadı: {string.Join("; ", created.Errors.Select(e => e.Description))}");

        var added = await userManager.AddToRoleAsync(admin, HastaneRoles.Admin);
        if (!added.Succeeded)
        {
            await userManager.DeleteAsync(admin);
            throw new InvalidOperationException(
                $"Admin rolü atanamadı: {string.Join("; ", added.Errors.Select(e => e.Description))}");
        }

        logger.LogInformation("İlk Admin oluşturuldu: {Email}", email);
    }
}
