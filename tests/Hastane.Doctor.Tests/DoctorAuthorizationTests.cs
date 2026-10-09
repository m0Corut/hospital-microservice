using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using static Hastane.Doctor.Tests.TestSupport;

namespace Hastane.Doctor.Tests;

// JWT-TASARIM.md §5/§7: liste/search/batch anonim, {id} ayrıntısı (telefon + e-posta) giriş yapmış herkese,
// POST/DELETE Admin, PUT Admin ya da kendi kaydındaki Doctor (rol + doctor_id eşleşmesi).
public class DoctorAuthorizationTests : IClassFixture<DoctorApiFactory>
{
    private const string UnknownId = "64b7f0f0f0f0f0f0f0f0f0f0";

    private readonly DoctorApiFactory _factory;

    public DoctorAuthorizationTests(DoctorApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientWith(string? token)
    {
        var client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    private static string AdminToken => TestJwt.Create("admin-1", [HastaneRoles.Admin]);

    private static string DoctorToken(string? doctorId) => TestJwt.Create("doctor-user", [HastaneRoles.Doctor], doctorId);

    private static string PatientToken => TestJwt.Create("patient-1", [HastaneRoles.Patient]);

    private Task<ResultDoctorDto> CreateDoctorAsync(string last = "Yetki")
        => ClientWith(AdminToken).CreateOk(NewDoc(last: last), Ct);

    private static UpdateDoctorDto UpdateOf(string id, string lastName = "Güncel") => new()
    {
        Id = id,
        FirstName = "Ayşe",
        LastName = lastName,
        Gender = DoctorGender.Female,
        BranchId = "b",
    };

    private async Task<string> LastNameAsync(string id)
    {
        var json = await ClientWith(AdminToken).GetStringAsync($"/api/doctors/{id}", Ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("lastName").GetString()!;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string id, string lastName = "Güncel")
        => client.PutAsJsonAsync($"/api/doctors/{id}", UpdateOf(id, lastName), Json, Ct);

    // ---- kapsam ----

    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(_factory.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    // ---- anonim uçlar ----

    [Theory]
    [InlineData("/api/doctors")]
    [InlineData("/api/doctors?branchId=x")]
    [InlineData("/api/doctors/search")]
    [InlineData("/api/doctors/batch?ids=64b7f0f0f0f0f0f0f0f0f0f0")]
    public async Task AnonymousEndpoints_ListSearchBatch_Return200WithoutToken(string url)
        => Assert.Equal(HttpStatusCode.OK, (await ClientWith(null).GetAsync(url, Ct)).StatusCode);

    // ---- GET {id}: giriş yapmış herkes ----

    [Fact]
    public async Task GetById_WithoutToken_Returns401()
    {
        var created = await CreateDoctorAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(null).GetAsync($"/api/doctors/{created.Id}", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("broken-signature")]
    [InlineData("alg-none")]
    [InlineData("hs256-public-key-as-secret")]
    public async Task GetById_WithInvalidToken_Returns401(string kind)
    {
        var created = await CreateDoctorAsync();
        var token = BadToken(kind);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(token).GetAsync($"/api/doctors/{created.Id}", Ct)).StatusCode);
    }

    private static string BadToken(string kind)
    {
        var roles = new[] { HastaneRoles.Admin }; // Admin iddiası bile sahte/süresi dolmuş token'ı kurtarmaz
        return kind switch
        {
            "expired" => TestJwt.CreateExpired("admin-1", roles),
            "broken-signature" => TestJwt.CreateWithBrokenSignature("admin-1", roles),
            "alg-none" => TestJwt.CreateAlgNone("admin-1", roles),
            _ => TestJwt.CreateHs256WithPublicKeyAsSecret("admin-1", roles),
        };
    }

    [Theory]
    [InlineData(HastaneRoles.Patient)]
    [InlineData(HastaneRoles.Doctor)]
    [InlineData(HastaneRoles.Admin)]
    public async Task GetById_WithAnyLoggedInRole_Returns200_WithPhoneAndEmail(string role)
    {
        var created = await CreateDoctorAsync();
        var token = TestJwt.Create("user-1", [role]);

        var response = await ClientWith(token).GetAsync($"/api/doctors/{created.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("gizli@hastane.test", doc.RootElement.GetProperty("email").GetString());
        Assert.Equal("0555 000 00 00", doc.RootElement.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task GetById_UnknownId_WithToken_Returns404()
        => Assert.Equal(HttpStatusCode.NotFound, (await ClientWith(PatientToken).GetAsync($"/api/doctors/{UnknownId}", Ct)).StatusCode);

    // ---- POST / DELETE: yalnız Admin ----

    [Fact]
    public async Task Post_WithoutToken_Returns401()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await ClientWith(null).PostAsJsonAsync("/api/doctors", NewDoc(), Json, Ct)).StatusCode);

    [Theory]
    [InlineData("expired")]
    [InlineData("broken-signature")]
    [InlineData("alg-none")]
    [InlineData("hs256-public-key-as-secret")]
    public async Task PostPutDelete_WithInvalidToken_Return401(string kind)
    {
        var client = ClientWith(BadToken(kind));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/doctors", NewDoc(), Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAsync(client, UnknownId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/doctors/{UnknownId}", Ct)).StatusCode);
    }

    [Theory]
    [InlineData(HastaneRoles.Patient)]
    [InlineData(HastaneRoles.Doctor)]
    public async Task PostAndDelete_WithNonAdminRole_Return403(string role)
    {
        var created = await CreateDoctorAsync();
        // Doctor, kendi kaydının sahibi bile olsa silemez/oluşturamaz: bu uçlar rol düzeyinde Admin'e özel.
        var client = ClientWith(TestJwt.Create("user-1", [role], doctorId: role == HastaneRoles.Doctor ? created.Id : null));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/doctors", NewDoc(), Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/doctors/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientWith(AdminToken).GetAsync($"/api/doctors/{created.Id}", Ct)).StatusCode); // silinmedi
    }

    [Fact]
    public async Task PostAndDelete_WithAdmin_Succeed()
    {
        var admin = ClientWith(AdminToken);
        var created = await admin.CreateOk(NewDoc(last: "AdminSiler"), Ct);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/doctors/{created.Id}", Ct)).StatusCode);
    }

    // ---- PUT {id}: Admin ya da (Doctor rolü VE doctor_id == {id}) ----

    [Fact]
    public async Task Put_WithoutToken_Returns401()
    {
        var created = await CreateDoctorAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAsync(ClientWith(null), created.Id)).StatusCode);
    }

    [Fact]
    public async Task Put_ByPatient_Returns403()
    {
        var created = await CreateDoctorAsync("PutHasta");
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(ClientWith(PatientToken), created.Id)).StatusCode);
        Assert.Equal("PutHasta", await LastNameAsync(created.Id));
    }

    [Fact]
    public async Task Put_ByPatient_EvenWithDoctorIdClaimMatching_Returns403()
    {
        // doctor_id claim'i tek başına yetmez: rol de Doctor olmalı.
        var created = await CreateDoctorAsync("PutHastaClaim");
        var token = TestJwt.Create("patient-1", [HastaneRoles.Patient], doctorId: created.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(ClientWith(token), created.Id)).StatusCode);
    }

    [Fact]
    public async Task Put_ByDoctorWithMatchingDoctorId_Returns204_AndChangesRecord()
    {
        var created = await CreateDoctorAsync("PutSahip");

        var put = await PutAsync(ClientWith(DoctorToken(created.Id)), created.Id, "SahipGuncelledi");

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal("SahipGuncelledi", await LastNameAsync(created.Id));
    }

    [Fact]
    public async Task Put_ByDoctorWithDifferentDoctorId_Returns403_AndRecordUnchanged()
    {
        var mine = await CreateDoctorAsync("Benim");
        var other = await CreateDoctorAsync("Baskasi");

        var put = await PutAsync(ClientWith(DoctorToken(mine.Id)), other.Id, "Ele Gecirildi");

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal("Baskasi", await LastNameAsync(other.Id));
    }

    [Fact]
    public async Task Put_ByDoctorWithoutDoctorIdClaim_Returns403()
    {
        // Admin henüz bu Doctor kullanıcısını bir doktor kaydına bağlamamış: hiçbir kaydı güncelleyemez.
        var created = await CreateDoctorAsync("Bagsiz");

        var put = await PutAsync(ClientWith(DoctorToken(null)), created.Id);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal("Bagsiz", await LastNameAsync(created.Id));
    }

    [Fact]
    public async Task Put_ByDoctorWithEmptyDoctorIdClaim_Returns403()
    {
        var created = await CreateDoctorAsync("BosClaim");
        var token = TestJwt.Create("doctor-user", [HastaneRoles.Doctor], doctorId: "");

        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(ClientWith(token), created.Id)).StatusCode);
    }

    [Fact]
    public async Task Put_ByAdmin_Returns204_ForAnyDoctor()
    {
        var created = await CreateDoctorAsync("AdminPut");

        var put = await PutAsync(ClientWith(AdminToken), created.Id, "AdminGuncelledi");

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal("AdminGuncelledi", await LastNameAsync(created.Id));
    }

    [Fact]
    public async Task Put_ByOwnerDoctor_ForMissingRecord_Returns404()
    {
        // Sahibi olduğu id'de kayıt yoksa (silinmiş) olağan "bulunamadı".
        var put = await PutAsync(ClientWith(DoctorToken(UnknownId)), UnknownId);

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }
}
