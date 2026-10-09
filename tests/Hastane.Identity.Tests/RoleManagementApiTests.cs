using System.Net;
using System.Net.Http.Json;
using Hastane.Identity.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Identity.Tests;

public class RoleManagementApiTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _admin;

    public RoleManagementApiTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _admin = IdentityTestData.AdminClient(factory);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewRoleName() => "Rol" + Guid.NewGuid().ToString("N")[..10];

    // --- Yetki ---

    [Fact]
    public async Task Roles_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/roles", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(HastaneRoles.Patient)]
    [InlineData(HastaneRoles.Doctor)]
    public async Task RoleEndpoints_NonAdmin_Return403WithProblemDetails(string role)
    {
        var client = IdentityTestData.ClientWithRoles(_factory, role);
        var someUser = Guid.NewGuid();

        var list = await client.GetAsync("/api/roles", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal("application/problem+json", list.Content.Headers.ContentType!.MediaType);
        Assert.Contains("\"status\":403", await list.Content.ReadAsStringAsync(Ct));

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/roles", new { name = NewRoleName() }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/users/{someUser}/roles", new { role = "Doctor" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"/api/users/{someUser}/roles/Doctor", Ct)).StatusCode);
    }

    // --- Rol oluşturma / listeleme ---

    [Fact]
    public async Task Admin_ListsRoles_IncludingSystemRoles()
    {
        var roles = await _admin.GetFromJsonAsync<List<RoleDto>>("/api/roles", Ct);
        Assert.Contains(roles!, r => r.Name == "Admin");
        Assert.Contains(roles!, r => r.Name == "Doctor");
        Assert.Contains(roles!, r => r.Name == "Patient");
    }

    [Fact]
    public async Task Admin_CreatesRole_ThenDuplicateReturns409_EvenWithDifferentCase()
    {
        var name = NewRoleName();
        var created = await _admin.PostAsJsonAsync("/api/roles", new { name }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(name, (await created.Content.ReadFromJsonAsync<RoleDto>(Ct))!.Name);

        var roles = await _admin.GetFromJsonAsync<List<RoleDto>>("/api/roles", Ct);
        Assert.Contains(roles!, r => r.Name == name);

        var again = await _admin.PostAsJsonAsync("/api/roles", new { name = name.ToUpperInvariant() }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("iki kelime")]
    [InlineData("tire-var")]
    [InlineData("<script>")]
    public async Task CreateRole_InvalidName_Returns400(string name)
    {
        var response = await _admin.PostAsJsonAsync("/api/roles", new { name }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRole_TooLongName_Returns400()
    {
        var response = await _admin.PostAsJsonAsync("/api/roles", new { name = new string('a', 51) }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Kullanıcıya rol atama / kaldırma ---

    [Fact]
    public async Task Admin_AssignsAndRemovesRole()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);

        var assigned = await _admin.PostAsJsonAsync($"/api/users/{user.Id}/roles", new { role = "Doctor" }, Ct);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var dto = (await assigned.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!;
        Assert.Equal(["Doctor", "Patient"], dto.Roles);

        var removed = await _admin.DeleteAsync($"/api/users/{user.Id}/roles/Doctor", Ct);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(["Patient"], (await removed.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!.Roles);
        Assert.Equal(["Patient"], await IdentityTestData.GetRolesAsync(_factory, user.Id));
    }

    [Fact]
    public async Task Admin_CanAssignCustomRole_AndNewTokenCarriesIt()
    {
        var name = NewRoleName();
        await _admin.PostAsJsonAsync("/api/roles", new { name }, Ct);
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);

        var assigned = await _admin.PostAsJsonAsync($"/api/users/{user.Id}/roles", new { role = name }, Ct);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = user.Email, password = IdentityTestData.GoodPassword }, Ct);
        var result = (await login.Content.ReadFromJsonAsync<LoginResultDto>(Ct))!;
        var jwt = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(result.AccessToken);
        Assert.Contains(jwt.Claims, c => c.Type == "role" && c.Value == name);
    }

    [Fact]
    public async Task AssignRole_UnknownRole_Returns404()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var response = await _admin.PostAsJsonAsync($"/api/users/{user.Id}/roles", new { role = "YokBoyleRol" }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("bozuk-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task AssignAndRemoveRole_UnknownUser_Returns404(string id)
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.PostAsJsonAsync($"/api/users/{id}/roles", new { role = "Doctor" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/users/{id}/roles/Doctor", Ct)).StatusCode);
    }

    [Fact]
    public async Task AssignRole_AlreadyInRole_Returns409()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var response = await _admin.PostAsJsonAsync($"/api/users/{user.Id}/roles", new { role = "Patient" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AssignRole_MissingRoleInBody_Returns400()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var response = await _admin.PostAsJsonAsync($"/api/users/{user.Id}/roles", new { role = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveRole_UserNotInThatRole_Returns404()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var response = await _admin.DeleteAsync($"/api/users/{user.Id}/roles/Doctor", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveRole_UnknownRole_Returns404()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var response = await _admin.DeleteAsync($"/api/users/{user.Id}/roles/YokBoyleRol", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- Son Admin koruması ---

    [Fact]
    public async Task RemoveAdminRole_FromLastAdmin_Returns409_AndRoleStays()
    {
        var onlyAdmin = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Admin]);
        await IdentityTestData.MakeOnlyAdminAsync(_factory, onlyAdmin.Id);
        Assert.Equal(1, await IdentityTestData.CountAdminsAsync(_factory));

        var response = await _admin.DeleteAsync($"/api/users/{onlyAdmin.Id}/roles/Admin", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(HastaneRoles.Admin, await IdentityTestData.GetRolesAsync(_factory, onlyAdmin.Id));
    }

    [Fact]
    public async Task LastAdmin_CannotDemoteSelf()
    {
        var onlyAdmin = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Admin]);
        await IdentityTestData.MakeOnlyAdminAsync(_factory, onlyAdmin.Id);
        var self = IdentityTestData.ClientFor(_factory, onlyAdmin, HastaneRoles.Admin);

        var response = await self.DeleteAsync($"/api/users/{onlyAdmin.Id}/roles/Admin", Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAdminRole_WhenAnotherAdminExists_Succeeds_ThenLastOneIsProtected()
    {
        var first = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Admin]);
        var second = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Admin]);
        await IdentityTestData.MakeOnlyAdminAsync(_factory, first.Id);
        await _admin.PostAsJsonAsync($"/api/users/{second.Id}/roles", new { role = "Admin" }, Ct);
        Assert.Equal(2, await IdentityTestData.CountAdminsAsync(_factory));

        var removeFirst = await _admin.DeleteAsync($"/api/users/{first.Id}/roles/Admin", Ct);
        Assert.Equal(HttpStatusCode.OK, removeFirst.StatusCode);

        var removeSecond = await _admin.DeleteAsync($"/api/users/{second.Id}/roles/Admin", Ct);
        Assert.Equal(HttpStatusCode.Conflict, removeSecond.StatusCode);
        Assert.Equal(1, await IdentityTestData.CountAdminsAsync(_factory));
    }
}
