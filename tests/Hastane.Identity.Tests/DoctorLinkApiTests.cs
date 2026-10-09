using System.Net;
using System.Net.Http.Json;
using Hastane.Identity.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Hastane.Identity.Tests;

public class DoctorLinkApiTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _admin;

    public DoctorLinkApiTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _admin = IdentityTestData.AdminClient(factory);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewDoctorId() => Guid.NewGuid().ToString("N")[..24]; // MongoDB ObjectId boyu

    [Theory]
    [InlineData(HastaneRoles.Patient)]
    [InlineData(HastaneRoles.Doctor)]
    public async Task LinkDoctor_NonAdmin_Returns403(string role)
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        var client = IdentityTestData.ClientWithRoles(_factory, role);
        var response = await client.PutAsJsonAsync($"/api/users/{user.Id}/doctor-link", new { doctorId = NewDoctorId() }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LinkDoctor_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient()
            .PutAsJsonAsync($"/api/users/{Guid.NewGuid()}/doctor-link", new { doctorId = NewDoctorId() }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LinkDoctor_UserWithoutDoctorRole_Returns409()
    {
        var patient = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var response = await _admin.PutAsJsonAsync($"/api/users/{patient.Id}/doctor-link", new { doctorId = NewDoctorId() }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null((await IdentityTestData.GetUserAsync(_factory, patient.Id)).DoctorId);
    }

    [Fact]
    public async Task LinkDoctor_Succeeds_AndNextLoginTokenCarriesDoctorId()
    {
        var doctor = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        var doctorId = NewDoctorId();

        var response = await _admin.PutAsJsonAsync($"/api/users/{doctor.Id}/doctor-link", new { doctorId }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(doctorId, (await response.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!.DoctorId);

        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = doctor.Email, password = IdentityTestData.GoodPassword }, Ct);
        var token = (await login.Content.ReadFromJsonAsync<LoginResultDto>(Ct))!.AccessToken;
        Assert.Equal(doctorId, new JsonWebToken(token).GetClaim(HastaneClaims.DoctorId).Value);
    }

    [Fact]
    public async Task LinkDoctor_SameDoctorToTwoUsers_Returns409()
    {
        var first = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        var second = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        var doctorId = NewDoctorId();

        Assert.Equal(HttpStatusCode.OK,
            (await _admin.PutAsJsonAsync($"/api/users/{first.Id}/doctor-link", new { doctorId }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _admin.PutAsJsonAsync($"/api/users/{second.Id}/doctor-link", new { doctorId }, Ct)).StatusCode);
        Assert.Null((await IdentityTestData.GetUserAsync(_factory, second.Id)).DoctorId);
    }

    [Fact]
    public async Task LinkDoctor_SameUserSameDoctorAgain_IsIdempotent_AndCanBeChanged()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        var doctorId = NewDoctorId();
        await _admin.PutAsJsonAsync($"/api/users/{user.Id}/doctor-link", new { doctorId }, Ct);

        Assert.Equal(HttpStatusCode.OK,
            (await _admin.PutAsJsonAsync($"/api/users/{user.Id}/doctor-link", new { doctorId }, Ct)).StatusCode);

        var other = NewDoctorId();
        var changed = await _admin.PutAsJsonAsync($"/api/users/{user.Id}/doctor-link", new { doctorId = other }, Ct);
        Assert.Equal(other, (await changed.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!.DoctorId);
    }

    [Fact]
    public async Task LinkDoctor_UnknownUser_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.PutAsJsonAsync($"/api/users/{Guid.NewGuid()}/doctor-link", new { doctorId = NewDoctorId() }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.PutAsJsonAsync("/api/users/bozuk/doctor-link", new { doctorId = NewDoctorId() }, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    public async Task LinkDoctor_EmptyDoctorId_Returns400(string doctorId)
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        var response = await _admin.PutAsJsonAsync($"/api/users/{user.Id}/doctor-link", new { doctorId }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LinkDoctor_TooLongDoctorId_Returns400()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        var response = await _admin.PutAsJsonAsync($"/api/users/{user.Id}/doctor-link",
            new { doctorId = new string('a', 65) }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemovingDoctorRole_ClearsLink_SoDoctorCanBeLinkedToSomeoneElse()
    {
        var first = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        var second = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        var doctorId = NewDoctorId();
        await _admin.PutAsJsonAsync($"/api/users/{first.Id}/doctor-link", new { doctorId }, Ct);

        Assert.Equal(HttpStatusCode.OK, (await _admin.DeleteAsync($"/api/users/{first.Id}/roles/Doctor", Ct)).StatusCode);

        Assert.Null((await IdentityTestData.GetUserAsync(_factory, first.Id)).DoctorId);
        Assert.Equal(HttpStatusCode.OK,
            (await _admin.PutAsJsonAsync($"/api/users/{second.Id}/doctor-link", new { doctorId }, Ct)).StatusCode);
    }
}
