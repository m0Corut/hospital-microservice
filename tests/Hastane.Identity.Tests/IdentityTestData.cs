using Hastane.Identity.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Identity.Tests;

// Testlerde veritabanına doğrudan kullanıcı kurmak (API'den geçmeden) ve token üretmek için ortak yardımcılar.
public static class IdentityTestData
{
    public const string GoodPassword = "Passw0rd1";

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    public static async Task<AppUser> CreateUserAsync(WebApplicationFactory<Program> factory,
        string[] roles, string? doctorId = null, string? email = null, string password = GoodPassword)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        email ??= NewEmail();
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            FirstName = "Test",
            LastName = "Kullanıcı",
            CreatedAt = DateTime.UtcNow,
            DoctorId = doctorId,
        };
        var created = await userManager.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        foreach (var role in roles)
            Assert.True((await userManager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    public static async Task<AppUser> GetUserAsync(WebApplicationFactory<Program> factory, string id)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await userManager.FindByIdAsync(id))!;
    }

    public static async Task<IList<string>> GetRolesAsync(WebApplicationFactory<Program> factory, string id)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await userManager.GetRolesAsync((await userManager.FindByIdAsync(id))!);
    }

    // "Son Admin" senaryoları için: verilen kullanıcı dışındaki herkesin Admin rolünü doğrudan veritabanından alır.
    public static async Task MakeOnlyAdminAsync(WebApplicationFactory<Program> factory, string keepUserId)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        foreach (var admin in await userManager.GetUsersInRoleAsync(HastaneRoles.Admin))
            if (admin.Id != keepUserId)
                await userManager.RemoveFromRoleAsync(admin, HastaneRoles.Admin);
    }

    public static async Task<int> CountAdminsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await userManager.GetUsersInRoleAsync(HastaneRoles.Admin)).Count;
    }

    public static HttpClient ClientFor(WebApplicationFactory<Program> factory, AppUser user, params string[] roles)
        => factory.CreateClient().WithBearer(TestJwt.Create(user.Id, roles, user.DoctorId, user.Email));

    // Veritabanında olmayan, yalnız token'ı olan Admin (rol yönetimi uçlarına erişim için yeterli).
    public static HttpClient AdminClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient().WithBearer(TestJwt.Create(Guid.NewGuid().ToString(), [HastaneRoles.Admin]));

    public static HttpClient ClientWithRoles(WebApplicationFactory<Program> factory, params string[] roles)
        => factory.CreateClient().WithBearer(TestJwt.Create(Guid.NewGuid().ToString(), roles));
}
