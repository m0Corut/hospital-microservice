using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hastane.Identity.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Identity.Tests;

// Gece N3b: PUT auth/me, POST auth/change-password, GET users (admin listesi), DELETE users/{id}/doctor-link.
public class ProfileAndAdminListApiTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _anonymous;
    private readonly HttpClient _admin;

    public ProfileAndAdminListApiTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _anonymous = factory.CreateClient();
        _admin = IdentityTestData.AdminClient(factory);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- PUT /api/auth/me ----------

    [Fact]
    public async Task UpdateMe_Anonymous_Returns401()
    {
        var response = await _anonymous.PutAsJsonAsync("/api/auth/me", new { firstName = "A", lastName = "B" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(HastaneRoles.Patient)]
    [InlineData(HastaneRoles.Doctor)]
    [InlineData(HastaneRoles.Admin)]
    public async Task UpdateMe_AnyLoggedInRole_UpdatesOwnNames_AndTrims(string role)
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [role]);
        var client = IdentityTestData.ClientFor(_factory, user, role);

        var response = await client.PutAsJsonAsync("/api/auth/me", new { firstName = "  Yeni ", lastName = " Soyad  " }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!;
        Assert.Equal(user.Id, dto.Id);
        Assert.Equal("Yeni", dto.FirstName);
        Assert.Equal("Soyad", dto.LastName);
        var stored = await IdentityTestData.GetUserAsync(_factory, user.Id);
        Assert.Equal("Yeni", stored.FirstName);
    }

    [Fact]
    public async Task UpdateMe_IgnoresIdEmailAndRoleInBody_AndNeverTouchesOthers()
    {
        var me = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var other = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var client = IdentityTestData.ClientFor(_factory, me, HastaneRoles.Patient);

        var response = await client.PutAsJsonAsync("/api/auth/me", new
        {
            firstName = "Ben",
            lastName = "Yeni",
            id = other.Id,
            email = "hack@example.com",
            roles = new[] { HastaneRoles.Admin },
            doctorId = "abc",
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var storedMe = await IdentityTestData.GetUserAsync(_factory, me.Id);
        Assert.Equal(me.Email, storedMe.Email);
        Assert.Null(storedMe.DoctorId);
        Assert.Equal(["Patient"], await IdentityTestData.GetRolesAsync(_factory, me.Id));
        var storedOther = await IdentityTestData.GetUserAsync(_factory, other.Id);
        Assert.Equal("Test", storedOther.FirstName);
    }

    [Theory]
    [InlineData("", "Soyad")]
    [InlineData("Ad", "")]
    [InlineData("   ", "Soyad")]
    public async Task UpdateMe_BlankNames_Returns400(string first, string last)
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var client = IdentityTestData.ClientFor(_factory, user, HastaneRoles.Patient);
        var response = await client.PutAsJsonAsync("/api/auth/me", new { firstName = first, lastName = last }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMe_TooLongName_Returns400()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var client = IdentityTestData.ClientFor(_factory, user, HastaneRoles.Patient);
        var response = await client.PutAsJsonAsync("/api/auth/me", new { firstName = new string('a', 101), lastName = "B" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- POST /api/auth/change-password ----------

    private async Task<HttpResponseMessage> LoginAsync(string email, string password)
        => await _anonymous.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);

    [Fact]
    public async Task ChangePassword_Anonymous_Returns401()
    {
        var response = await _anonymous.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "Passw0rd1", newPassword = "Newpassw0rd" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_Succeeds_204_OldPasswordStopsWorking_NewWorks()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var client = IdentityTestData.ClientFor(_factory, user, HastaneRoles.Patient);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = IdentityTestData.GoodPassword, newPassword = "Yenisifre9" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(user.Email!, IdentityTestData.GoodPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email!, "Yenisifre9")).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Returns400_GenericMessage_PasswordUnchanged()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var client = IdentityTestData.ClientFor(_factory, user, HastaneRoles.Patient);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "Yanlis1234", newPassword = "Yenisifre9" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Mevcut şifre hatalı", body);
        Assert.DoesNotContain("Yenisifre9", body);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email!, IdentityTestData.GoodPassword)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RepeatedWrongCurrent_LocksAccount_Then423_EvenWithCorrectPassword()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var client = IdentityTestData.ClientFor(_factory, user, HastaneRoles.Patient);

        // Identity hesabı 5. hatalı denemede kilitler ve o denemenin kendisi de "kilitli" döner (login ile aynı).
        for (var i = 0; i < 4; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = "Yanlis1234", newPassword = "Yenisifre9" }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }
        var fifth = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "Yanlis1234", newPassword = "Yenisifre9" }, Ct);
        Assert.Equal((HttpStatusCode)423, fifth.StatusCode);

        var locked = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = IdentityTestData.GoodPassword, newPassword = "Yenisifre9" }, Ct);
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
        // Giriş de aynı sayaçla kilitli.
        Assert.Equal((HttpStatusCode)423, (await LoginAsync(user.Email!, IdentityTestData.GoodPassword)).StatusCode);
    }

    [Theory]
    [InlineData("kisa1A")]
    [InlineData("sadecekucuk123")]
    [InlineData("SADECEBUYUK123")]
    [InlineData("RakamsizSifre")]
    public async Task ChangePassword_WeakNewPassword_Returns400_AndKeepsOld(string weak)
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var client = IdentityTestData.ClientFor(_factory, user, HastaneRoles.Patient);

        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = IdentityTestData.GoodPassword, newPassword = weak }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email!, IdentityTestData.GoodPassword)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_SameAsCurrent_Returns400()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var client = IdentityTestData.ClientFor(_factory, user, HastaneRoles.Patient);
        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = IdentityTestData.GoodPassword, newPassword = IdentityTestData.GoodPassword }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_MissingFields_Returns400()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var client = IdentityTestData.ClientFor(_factory, user, HastaneRoles.Patient);
        var response = await client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ForUserDeletedFromDb_Returns404()
    {
        // Token geçerli ama kullanıcı yok (eski token sınırı): 404, çökme yok.
        var client = IdentityTestData.ClientWithRoles(_factory, HastaneRoles.Patient);
        var response = await client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "Passw0rd1", newPassword = "Yenisifre9" }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- GET /api/users (admin listesi) ----------

    [Fact]
    public async Task ListUsers_Anonymous_Returns401()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/users?page=1", Ct)).StatusCode);

    [Theory]
    [InlineData(HastaneRoles.Patient)]
    [InlineData(HastaneRoles.Doctor)]
    public async Task ListUsers_NonAdmin_Returns403(string role)
    {
        var client = IdentityTestData.ClientWithRoles(_factory, role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users?page=1", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users", Ct)).StatusCode);
    }

    [Fact]
    public async Task ListUsers_Paging_ReturnsEnvelope_WithTotalCount_AndRolesPerUser()
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        for (var i = 0; i < 3; i++)
            await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient], email: $"{tag}-{i}@example.com");

        var response = await _admin.GetAsync($"/api/users?search={tag}&page=2&pageSize=2", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = (await response.Content.ReadFromJsonAsync<PagedResult<UserSummaryDto>>(Ct))!;
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        var only = Assert.Single(page.Items);
        Assert.Equal(["Patient"], only.Roles);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        foreach (var name in new[] { "items", "totalCount", "page", "pageSize" })
            Assert.True(json.RootElement.TryGetProperty(name, out _), name);
    }

    [Fact]
    public async Task ListUsers_SearchByEmailAndName_IsCaseInsensitive()
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        var byEmail = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient], email: $"Zeynep.{tag}@example.com");
        var other = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);

        var emailHit = (await _admin.GetFromJsonAsync<PagedResult<UserSummaryDto>>($"/api/users?search=ZEYNEP.{tag}", Ct))!;
        Assert.Equal(byEmail.Id, Assert.Single(emailHit.Items).Id);

        // Ad araması: "Test Kullanıcı" tüm test kullanıcılarında var; yeni ad verilen tek kişi bulunur.
        var uniqueName = "Ad" + tag;
        var client = IdentityTestData.ClientFor(_factory, other, HastaneRoles.Patient);
        await client.PutAsJsonAsync("/api/auth/me", new { firstName = uniqueName, lastName = "Soyadi" }, Ct);
        var nameHit = (await _admin.GetFromJsonAsync<PagedResult<UserSummaryDto>>($"/api/users?search={uniqueName.ToLowerInvariant()}", Ct))!;
        Assert.Equal(other.Id, Assert.Single(nameHit.Items).Id);
        var fullName = (await _admin.GetFromJsonAsync<PagedResult<UserSummaryDto>>(
            $"/api/users?search={Uri.EscapeDataString(uniqueName + " Soyadi")}", Ct))!;
        Assert.Single(fullName.Items);
    }

    [Fact]
    public async Task ListUsers_SearchWildcards_AreLiteral()
    {
        await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var page = (await _admin.GetFromJsonAsync<PagedResult<UserSummaryDto>>(
            $"/api/users?search={Uri.EscapeDataString("%")}", Ct))!;
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task ListUsers_RoleFilter_ReturnsOnlyThatRole_AndUnknownRoleIsEmpty()
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        var doctor = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor, HastaneRoles.Patient], email: $"dr-{tag}@example.com");
        await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient], email: $"pt-{tag}@example.com");

        var doctors = (await _admin.GetFromJsonAsync<PagedResult<UserSummaryDto>>($"/api/users?role=doctor&search={tag}", Ct))!;
        var hit = Assert.Single(doctors.Items);
        Assert.Equal(doctor.Id, hit.Id);
        // Süzme tek role göre, ama kullanıcının TÜM rolleri döner.
        Assert.Equal(["Doctor", "Patient"], hit.Roles);

        var none = (await _admin.GetFromJsonAsync<PagedResult<UserSummaryDto>>($"/api/users?role=YokBoyleRol&search={tag}", Ct))!;
        Assert.Equal(0, none.TotalCount);
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task ListUsers_ResponseHasNoSensitiveFields()
    {
        await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var text = await (await _admin.GetAsync("/api/users", Ct)).Content.ReadAsStringAsync(Ct);
        foreach (var forbidden in new[] { "passwordHash", "securityStamp", "phoneNumber", "lockout" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=10001")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=-5")]
    public async Task ListUsers_InvalidPaging_Returns400(string query)
        => Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync($"/api/users?{query}", Ct)).StatusCode);

    [Fact]
    public async Task ListUsers_PageSizeMax100_IsAccepted()
        => Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync("/api/users?pageSize=100", Ct)).StatusCode);

    [Fact]
    public async Task ListUsers_TooLongSearch_Returns400()
        => Assert.Equal(HttpStatusCode.BadRequest,
            (await _admin.GetAsync($"/api/users?search={new string('a', 101)}", Ct)).StatusCode);

    [Fact]
    public async Task ListUsers_WithIds_KeepsOldFlatListBehavior_ForDoctor()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var doctor = IdentityTestData.ClientWithRoles(_factory, HastaneRoles.Doctor);

        var response = await doctor.GetAsync($"/api/users?ids={user.Id}&page=0&pageSize=999", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<UserSummaryDto>>(Ct);
        Assert.Equal(user.Id, Assert.Single(list!).Id);
    }

    // ---------- DELETE /api/users/{id}/doctor-link ----------

    [Fact]
    public async Task UnlinkDoctor_Anonymous_Returns401()
        => Assert.Equal(HttpStatusCode.Unauthorized,
            (await _anonymous.DeleteAsync($"/api/users/{Guid.NewGuid()}/doctor-link", Ct)).StatusCode);

    [Theory]
    [InlineData(HastaneRoles.Patient)]
    [InlineData(HastaneRoles.Doctor)]
    public async Task UnlinkDoctor_NonAdmin_Returns403(string role)
    {
        var client = IdentityTestData.ClientWithRoles(_factory, role);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.DeleteAsync($"/api/users/{Guid.NewGuid()}/doctor-link", Ct)).StatusCode);
    }

    [Fact]
    public async Task UnlinkDoctor_RemovesLink_204_AndDoctorRecordCanBeLinkedAgain()
    {
        var doctorId = Guid.NewGuid().ToString("N")[..24];
        var first = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor], doctorId);
        var second = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);

        var response = await _admin.DeleteAsync($"/api/users/{first.Id}/doctor-link", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await IdentityTestData.GetUserAsync(_factory, first.Id)).DoctorId);
        Assert.Equal(HttpStatusCode.OK,
            (await _admin.PutAsJsonAsync($"/api/users/{second.Id}/doctor-link", new { doctorId }, Ct)).StatusCode);
    }

    [Fact]
    public async Task UnlinkDoctor_NoLink_IsIdempotent204()
    {
        var doctor = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/users/{doctor.Id}/doctor-link", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/users/{doctor.Id}/doctor-link", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("bozuk-id")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public async Task UnlinkDoctor_UnknownUser_Returns404(string id)
        => Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/users/{id}/doctor-link", Ct)).StatusCode);
}
