using System.Net;
using System.Net.Http.Json;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Pharmacy.Tests;

// JWT-TASARIM.md: kimlik ve rol kuralları. Katalog okuma = giriş yapmış herkes; geri kalan her şey Admin.
[Collection(PharmacyCollection.Name)]
public class PharmacyAuthorizationTests : PharmacyTestBase
{
    private static readonly string AnyId = Guid.NewGuid().ToString();

    public PharmacyAuthorizationTests(PharmacyApiFactory factory) : base(factory)
    {
    }

    // Yalnız Admin'in girebildiği tüm uçlar (yöntem, yol).
    public static TheoryData<string, string> AdminOnlyEndpoints() => new()
    {
        { "POST", "/api/medicines" },
        { "PUT", $"/api/medicines/{AnyId}" },
        { "DELETE", $"/api/medicines/{AnyId}" },
        { "POST", $"/api/medicines/{AnyId}/stock" },
        { "GET", "/api/medicines/low-stock" },
        { "GET", $"/api/medicines/{AnyId}/movements" },
        { "POST", "/api/pharmacy/dispense" },
        { "GET", $"/api/pharmacy/dispense/preview/{AnyId}" },
        { "GET", "/api/pharmacy/dispensations" },
        { "GET", $"/api/pharmacy/dispensations/by-prescription/{AnyId}" },
    };

    // Giriş yapmış herkese açık uçlar.
    public static TheoryData<string> AuthenticatedEndpoints() => new()
    {
        "/api/medicines",
        $"/api/medicines/{AnyId}",
    };

    private static HttpRequestMessage Req(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT") request.Content = JsonContent.Create(new { });
        return request;
    }

    // --- 401 ---

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task AdminEndpoints_NoToken_Return401(string method, string url)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor().SendAsync(Req(method, url), Ct)).StatusCode);

    [Theory]
    [MemberData(nameof(AuthenticatedEndpoints))]
    public async Task ReadEndpoints_NoToken_Return401(string url)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor().GetAsync(url, Ct)).StatusCode);

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task AdminEndpoints_InvalidTokens_Return401(string method, string url)
    {
        var tokens = new[]
        {
            TestJwt.CreateExpired("u1", [HastaneRoles.Admin]),
            TestJwt.CreateWithBrokenSignature("u1", [HastaneRoles.Admin]),
            TestJwt.CreateAlgNone("u1", [HastaneRoles.Admin]),
            TestJwt.CreateHs256WithPublicKeyAsSecret("u1", [HastaneRoles.Admin]),
        };
        foreach (var token in tokens)
            Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(token).SendAsync(Req(method, url), Ct)).StatusCode);
    }

    // --- 403: Patient, Doctor ve rolsüz kullanıcı Admin uçlarına giremez ---

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task AdminEndpoints_PatientDoctorAndRoleless_Return403(string method, string url)
    {
        var tokens = new[]
        {
            TestJwt.Create(Unique("pat"), [HastaneRoles.Patient]),
            TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], doctorId: Unique("doc")),
            TestJwt.Create(Unique("usr"), []),
        };
        foreach (var token in tokens)
            Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(token).SendAsync(Req(method, url), Ct)).StatusCode);
    }

    [Fact]
    public async Task Forbidden_Dispense_NeverReachesPrescriptionService_OrChangesStock()
    {
        var medicine = await CreateMedicineAsync(stock: 5);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name);
        var patient = ClientFor(TestJwt.Create(Unique("pat"), [HastaneRoles.Patient]));

        var response = await DispenseAsync(prescriptionId, patient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, FakePrescriptionService.CallCount(prescriptionId));
        Assert.Equal(5, (await GetMedicineAsync(medicine.Id)).Stock);
    }

    [Fact]
    public async Task Patient_CannotCreateUpdateDeleteOrAdjust_NothingChanges()
    {
        var medicine = await CreateMedicineAsync(stock: 5);
        var patient = ClientFor(TestJwt.Create(Unique("pat"), [HastaneRoles.Patient]));

        var create = await patient.PostAsJsonAsync("/api/medicines", NewMedicineBody(), Json, Ct);
        var delete = await patient.DeleteAsync($"/api/medicines/{medicine.Id}", Ct);
        var adjust = await patient.PostAsJsonAsync($"/api/medicines/{medicine.Id}/stock", new { delta = -5, reason = "x" }, Json, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, adjust.StatusCode);
        Assert.Equal(5, (await GetMedicineAsync(medicine.Id)).Stock);
    }

    // --- Okuma: giriş yapmış herkes ---

    [Fact]
    public async Task Catalog_IsReadableByEveryAuthenticatedUser()
    {
        var medicine = await CreateMedicineAsync();
        var clients = new[]
        {
            ClientFor(TestJwt.Create(Unique("pat"), [HastaneRoles.Patient])),
            ClientFor(TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], doctorId: Unique("doc"))),
            ClientFor(TestJwt.Create(Unique("usr"), [])),
            ClientFor(AdminJwt),
        };

        foreach (var client in clients)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/medicines", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/medicines/{medicine.Id}", Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Admin_CanUseEveryAdminEndpoint()
    {
        var medicine = await CreateMedicineAsync(stock: 3);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name);

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/medicines/low-stock", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/medicines/{medicine.Id}/movements", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/pharmacy/dispensations", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/pharmacy/dispense/preview/{prescriptionId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await DispenseAsync(prescriptionId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/pharmacy/dispensations/by-prescription/{prescriptionId}", Ct)).StatusCode);
    }
}
