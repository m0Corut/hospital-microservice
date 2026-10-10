using System.Net;
using System.Net.Http.Json;
using Hastane.Identity.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Identity.Tests;

public class DoctorApplicationApiTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _anon;
    private readonly HttpClient _admin;

    public DoctorApplicationApiTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _anon = factory.CreateClient();
        _admin = IdentityTestData.AdminClient(factory);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Body(string email, string license = "TR-12345", string? note = "Kardiyoloji uzmanıyım.", string? branchId = "branch-1")
        => new { firstName = "Mehmet", lastName = "Doktor", email, password = IdentityTestData.GoodPassword, branchId, licenseNumber = license, note };

    private async Task<(string Email, DoctorApplicationDto Application)> ApplyAsync()
    {
        var email = IdentityTestData.NewEmail();
        var response = await _anon.PostAsJsonAsync("/api/auth/register-doctor", Body(email), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (email, (await response.Content.ReadFromJsonAsync<DoctorApplicationDto>(Ct))!);
    }

    private async Task<LoginResultDto> LoginAsync(string email)
    {
        var response = await _anon.PostAsJsonAsync("/api/auth/login", new { email, password = IdentityTestData.GoodPassword }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResultDto>(Ct))!;
    }

    [Fact]
    public async Task RegisterDoctor_CreatesPatientUser_AndPendingApplication()
    {
        var (email, application) = await ApplyAsync();

        Assert.Equal("Pending", application.Status);
        Assert.Equal("TR-12345", application.LicenseNumber);
        Assert.Equal("branch-1", application.BranchId);
        Assert.Equal(email, application.Email);
        Assert.Null(application.DecidedAt);

        var login = await LoginAsync(email); // giriş yapabilir
        Assert.Equal(["Patient"], login.User.Roles);
        Assert.Equal(application.UserId, login.User.Id);
    }

    [Fact]
    public async Task RegisterDoctor_DuplicateEmail_Returns409()
    {
        var (email, _) = await ApplyAsync();

        var again = await _anon.PostAsJsonAsync("/api/auth/register-doctor", Body(email), Ct);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task RegisterDoctor_EmailAlreadyRegisteredAsPatient_Returns409()
    {
        var patient = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);

        var response = await _anon.PostAsJsonAsync("/api/auth/register-doctor", Body(patient.Email!), Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789012345678901234567890123456789012345678901")] // 51
    public async Task RegisterDoctor_BadLicense_Returns400_AndCreatesNoUser(string license)
    {
        var email = IdentityTestData.NewEmail();

        var response = await _anon.PostAsJsonAsync("/api/auth/register-doctor", Body(email, license), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _anon.PostAsJsonAsync("/api/auth/login", new { email, password = IdentityTestData.GoodPassword }, Ct)).StatusCode);
    }

    [Fact]
    public async Task RegisterDoctor_NoteTooLong_Returns400()
    {
        var response = await _anon.PostAsJsonAsync("/api/auth/register-doctor",
            Body(IdentityTestData.NewEmail(), note: new string('x', 501)), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDoctor_WeakPassword_Returns400_AndCreatesNoApplication()
    {
        var email = IdentityTestData.NewEmail();
        var response = await _anon.PostAsJsonAsync("/api/auth/register-doctor",
            new { firstName = "A", lastName = "B", email, password = "kisa", licenseNumber = "L1" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var pending = await _admin.GetFromJsonAsync<PagedResult<DoctorApplicationDto>>("/api/doctor-applications?status=Pending&pageSize=100", Ct);
        Assert.DoesNotContain(pending!.Items, a => a.Email == email);
    }

    [Fact]
    public async Task RegisterDoctor_CannotSelfAssignRoleOrStatus()
    {
        var email = IdentityTestData.NewEmail();
        var response = await _anon.PostAsJsonAsync("/api/auth/register-doctor",
            new { firstName = "A", lastName = "B", email, password = IdentityTestData.GoodPassword, licenseNumber = "L1", role = "Admin", roles = new[] { "Admin" }, status = "Approved" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Pending", (await response.Content.ReadFromJsonAsync<DoctorApplicationDto>(Ct))!.Status);
        Assert.Equal(["Patient"], (await LoginAsync(email)).User.Roles);
    }

    [Fact]
    public async Task MyApplication_ShowsOwnStatus_AndIs404WithoutOne()
    {
        var (email, application) = await ApplyAsync();
        var login = await LoginAsync(email);
        var client = _factory.CreateClient().WithBearer(login.AccessToken);

        var mine = await client.GetFromJsonAsync<DoctorApplicationDto>("/api/auth/me/doctor-application", Ct);
        Assert.Equal(application.Id, mine!.Id);
        Assert.Equal("Pending", mine.Status);

        var plain = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var response = await IdentityTestData.ClientFor(_factory, plain, HastaneRoles.Patient)
            .GetAsync("/api/auth/me/doctor-application", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.GetAsync("/api/auth/me/doctor-application", Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_ListsApplications_WithNameEmail_FilterAndPaging()
    {
        var (email, application) = await ApplyAsync();
        await ApplyAsync();

        var all = await _admin.GetFromJsonAsync<PagedResult<DoctorApplicationDto>>("/api/doctor-applications?status=Pending&pageSize=100", Ct);
        var mine = Assert.Single(all!.Items, a => a.Id == application.Id);
        Assert.Equal(email, mine.Email);
        Assert.Equal("Mehmet", mine.FirstName);
        Assert.True(all.TotalCount >= 2);

        var paged = await _admin.GetFromJsonAsync<PagedResult<DoctorApplicationDto>>("/api/doctor-applications?page=1&pageSize=1", Ct);
        Assert.Single(paged!.Items);
        Assert.Equal(1, paged.PageSize);

        var approvedOnly = await _admin.GetFromJsonAsync<PagedResult<DoctorApplicationDto>>("/api/doctor-applications?status=Approved&pageSize=100", Ct);
        Assert.DoesNotContain(approvedOnly!.Items, a => a.Id == application.Id);
    }

    [Theory]
    [InlineData("status=Bogus")]
    [InlineData("status=7")]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    public async Task Admin_List_InvalidQuery_Returns400(string query)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync($"/api/doctor-applications?{query}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Approve_AddsDoctorRole_RemovesPatientRole_AndSecondDecisionIs409()
    {
        var (email, application) = await ApplyAsync();

        var approve = await _admin.PostAsync($"/api/doctor-applications/{application.Id}/approve", null, Ct);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var decided = (await approve.Content.ReadFromJsonAsync<DoctorApplicationDto>(Ct))!;
        Assert.Equal("Approved", decided.Status);
        Assert.NotNull(decided.DecidedAt);
        Assert.NotNull(decided.DecidedBy);

        Assert.Equal([HastaneRoles.Doctor], (await IdentityTestData.GetRolesAsync(_factory, application.UserId)).ToArray());
        Assert.Equal(["Doctor"], (await LoginAsync(email)).User.Roles);

        Assert.Equal(HttpStatusCode.Conflict,
            (await _admin.PostAsync($"/api/doctor-applications/{application.Id}/approve", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _admin.PostAsJsonAsync($"/api/doctor-applications/{application.Id}/reject", new { note = "geç kaldın" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Reject_RecordsNote_KeepsPatientRole_AndCannotBeApprovedAfterwards()
    {
        var (_, application) = await ApplyAsync();

        var reject = await _admin.PostAsJsonAsync($"/api/doctor-applications/{application.Id}/reject", new { note = "Belgeler eksik." }, Ct);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        var decided = (await reject.Content.ReadFromJsonAsync<DoctorApplicationDto>(Ct))!;
        Assert.Equal("Rejected", decided.Status);
        Assert.Equal("Belgeler eksik.", decided.DecisionNote);

        Assert.Equal([HastaneRoles.Patient], (await IdentityTestData.GetRolesAsync(_factory, application.UserId)).ToArray());
        Assert.Equal(HttpStatusCode.Conflict,
            (await _admin.PostAsync($"/api/doctor-applications/{application.Id}/approve", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Reject_WithoutNote_Returns400()
    {
        var (_, application) = await ApplyAsync();
        var response = await _admin.PostAsJsonAsync($"/api/doctor-applications/{application.Id}/reject", new { note = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Decide_UnknownApplication_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.PostAsync($"/api/doctor-applications/{Guid.NewGuid()}/approve", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _admin.PostAsJsonAsync($"/api/doctor-applications/{Guid.NewGuid()}/reject", new { note = "x" }, Ct)).StatusCode);
    }

    [Fact]
    public async Task Approve_ConcurrentRequests_OnlyOneSucceeds()
    {
        var (_, application) = await ApplyAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => _admin.PostAsync($"/api/doctor-applications/{application.Id}/approve", null, Ct)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal([HastaneRoles.Doctor], (await IdentityTestData.GetRolesAsync(_factory, application.UserId)).ToArray());
    }

    [Theory]
    [InlineData(HastaneRoles.Patient)]
    [InlineData(HastaneRoles.Doctor)]
    public async Task AdminEndpoints_NonAdmin_Return403(string role)
    {
        var (_, application) = await ApplyAsync();
        var client = IdentityTestData.ClientWithRoles(_factory, role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/doctor-applications", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/doctor-applications/{application.Id}/approve", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/doctor-applications/{application.Id}/reject", new { note = "x" }, Ct)).StatusCode);

        // Hiçbir şey değişmedi.
        Assert.Equal([HastaneRoles.Patient], (await IdentityTestData.GetRolesAsync(_factory, application.UserId)).ToArray());
    }

    [Fact]
    public async Task AdminEndpoints_WithoutToken_Return401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anon.GetAsync("/api/doctor-applications", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _anon.PostAsync($"/api/doctor-applications/{Guid.NewGuid()}/approve", null, Ct)).StatusCode);
    }
}
