using System.Net;
using System.Net.Http.Json;
using Hastane.Identity.Api.Data;
using Hastane.Identity.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Identity.Tests;

// Seed değerleri VERİLMİŞ açılış.
public class AdminSeedTests : IClassFixture<AdminSeedApiFactory>
{
    private readonly AdminSeedApiFactory _factory;

    public AdminSeedTests(AdminSeedApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Startup_CreatesFirstAdmin_WhoCanLoginAndManageRoles()
    {
        var client = _factory.CreateClient(); // uygulama açılır, tohumlama çalışır
        Assert.Equal(1, await IdentityTestData.CountAdminsAsync(_factory));

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = IdentityApiFactory.SeedAdminEmail, password = IdentityApiFactory.SeedAdminPassword }, Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var result = (await login.Content.ReadFromJsonAsync<LoginResultDto>(Ct))!;
        Assert.Equal([HastaneRoles.Admin], result.User.Roles);

        // Gerçek giriş token'ıyla Admin ucu açılır.
        var roles = await client.WithBearer(result.AccessToken).GetAsync("/api/roles", Ct);
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
    }

    [Fact]
    public async Task SeedingAgain_DoesNotCreateAnotherAdmin()
    {
        _factory.CreateClient();

        await AdminSeeder.SeedAsync(_factory.Services);
        await AdminSeeder.SeedAsync(_factory.Services, "baska-admin@example.com", "Adm1nPassw0rd");

        Assert.Equal(1, await IdentityTestData.CountAdminsAsync(_factory));
    }
}

// Seed değerleri YOK: normal çalışma (Admin zaten var ya da bilerek yok).
public class AdminSeedSkippedTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;

    public AdminSeedSkippedTests(IdentityApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutSeedValues_NoAdminIsCreated_AndStartupSucceeds()
    {
        _factory.CreateClient();
        Assert.Equal(0, await IdentityTestData.CountAdminsAsync(_factory));

        // Eksik değer(ler) sessizce atlanır.
        await AdminSeeder.SeedAsync(_factory.Services, "x@example.com", null);
        await AdminSeeder.SeedAsync(_factory.Services, null, "Adm1nPassw0rd");
        await AdminSeeder.SeedAsync(_factory.Services, " ", " ");
        Assert.Equal(0, await IdentityTestData.CountAdminsAsync(_factory));
    }

    [Fact]
    public async Task SeedWithWeakPassword_FailsLoudly_AndLeavesNoHalfCreatedUser()
    {
        _factory.CreateClient();
        var email = $"zayif-{Guid.NewGuid():N}@example.com";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminSeeder.SeedAsync(_factory.Services, email, "kisa"));

        Assert.Contains("İlk Admin oluşturulamadı", ex.Message);
        Assert.DoesNotContain("kisa", ex.Message); // şifre mesaja sızmaz
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "kisa" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SeedWithEmailOfExistingUser_DoesNotPromoteThem()
    {
        var existing = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);

        await AdminSeeder.SeedAsync(_factory.Services, existing.Email, "Adm1nPassw0rd");

        Assert.Equal(["Patient"], await IdentityTestData.GetRolesAsync(_factory, existing.Id));
        Assert.Equal(0, await IdentityTestData.CountAdminsAsync(_factory));
    }
}
