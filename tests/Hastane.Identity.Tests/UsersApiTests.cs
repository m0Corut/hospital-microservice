using System.Net;
using System.Net.Http.Json;
using Hastane.Identity.Api.Data;
using Hastane.Identity.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Identity.Tests;

public class UsersApiTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _anonymous;
    private readonly HttpClient _admin;

    public UsersApiTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _anonymous = factory.CreateClient();
        _admin = IdentityTestData.AdminClient(factory);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<UserSummaryDto> RegisterAsync(string firstName)
    {
        var response = await _anonymous.PostAsJsonAsync("/api/auth/register",
            new { firstName, lastName = "Test", email = $"{Guid.NewGuid():N}@example.com", password = "Passw0rd1" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!;
    }

    [Fact]
    public async Task Roles_AreSeeded_AtStartup()
    {
        // İstemci oluşturuldu => uygulama açıldı => tohumlama çalıştı.
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Doctor", "Patient" })
            Assert.True(await roleManager.RoleExistsAsync(role), $"{role} rolü yok");
    }

    [Fact]
    public async Task RoleSeeder_IsIdempotent()
    {
        await RoleSeeder.SeedAsync(_factory.Services);
        await RoleSeeder.SeedAsync(_factory.Services);

        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        // Başka testler özel rol ekleyebilir; sistem rolleri tam üçer kez değil BİR kez var.
        foreach (var role in HastaneRoles.All)
            Assert.Single(roleManager.Roles.Where(r => r.Name == role));
    }

    [Fact]
    public async Task GetById_WithoutToken_Returns401()
    {
        var user = await RegisterAsync("Mehmet");
        var response = await _anonymous.GetAsync($"/api/users/{user.Id}", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsSummaryWithoutSecrets()
    {
        var user = await RegisterAsync("Mehmet");
        var response = await _admin.GetAsync($"/api/users/{user.Id}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var forbidden in new[] { "passwordhash", "securitystamp", "phonenumber", "lockout" })
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);

        var dto = await response.Content.ReadFromJsonAsync<UserSummaryDto>(Ct);
        Assert.Equal("Mehmet", dto!.FirstName);
        Assert.Equal(["Patient"], dto.Roles);
    }

    [Fact]
    public async Task GetById_Self_Returns200_ButOtherPatientsRecord_Returns404()
    {
        var me = await RegisterAsync("Ben");
        var other = await RegisterAsync("Başkası");
        var meClient = _factory.CreateClient().WithBearer(TestJwt.Create(me.Id, [HastaneRoles.Patient]));

        Assert.Equal(HttpStatusCode.OK, (await meClient.GetAsync($"/api/users/{me.Id}", Ct)).StatusCode);
        // Rol yetmiyor ama 403 değil 404: başkasının kaydının var olduğu sızmasın.
        Assert.Equal(HttpStatusCode.NotFound, (await meClient.GetAsync($"/api/users/{other.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task GetById_Doctor_CanReadAnyUser()
    {
        var user = await RegisterAsync("Hasta");
        var doctor = IdentityTestData.ClientWithRoles(_factory, HastaneRoles.Doctor);
        Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync($"/api/users/{user.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var response = await _admin.GetAsync($"/api/users/{Guid.NewGuid()}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("'; DROP TABLE AspNetUsers;--")]
    public async Task GetById_MalformedId_Returns404(string id)
    {
        var response = await _admin.GetAsync($"/api/users/{Uri.EscapeDataString(id)}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetByIds_ReturnsOnlyRequestedUsers()
    {
        var a = await RegisterAsync("Ali");
        var b = await RegisterAsync("Berk");
        await RegisterAsync("Can"); // istenmedi

        var list = await _admin.GetFromJsonAsync<List<UserSummaryDto>>(
            $"/api/users?ids={a.Id},{b.Id},{Guid.NewGuid()},bozuk-id", Ct);

        Assert.Equal(2, list!.Count);
        Assert.Contains(list, u => u.Id == a.Id && u.FirstName == "Ali");
        Assert.Contains(list, u => u.Id == b.Id && u.FirstName == "Berk");
        Assert.All(list, u => Assert.Equal(["Patient"], u.Roles));
    }

    [Fact]
    public async Task GetByIds_Authorization_Anonymous401_Patient403_Doctor200()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/users?ids=", Ct)).StatusCode);

        var patient = IdentityTestData.ClientWithRoles(_factory, HastaneRoles.Patient);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.GetAsync("/api/users?ids=", Ct)).StatusCode);

        var doctor = IdentityTestData.ClientWithRoles(_factory, HastaneRoles.Doctor);
        Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync("/api/users?ids=", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(",,,")]
    public async Task GetByIds_EmptyOrNoIds_ReturnsEmptyList(string ids)
    {
        var response = await _admin.GetAsync($"/api/users?ids={Uri.EscapeDataString(ids)}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<UserSummaryDto>>(Ct))!);
    }

    [Fact]
    public async Task GetUsers_WithoutIds_IsNowThePagedAdminList()
    {
        // Gece N3b: ids hiç verilmezse uç artık sayfalı admin listesidir (düz liste değil).
        var response = await _admin.GetAsync("/api/users", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<UserSummaryDto>>(Ct);
        Assert.Equal(1, page!.Page);
        Assert.Equal(20, page.PageSize);
    }

    [Fact]
    public async Task GetByIds_TooMany_Returns400()
    {
        var ids = string.Join(",", Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()));
        var response = await _admin.GetAsync($"/api/users?ids={ids}", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
