using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hastane.Prescription.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Prescription.Tests;

// JWT-TASARIM.md §5/§7: kimlik, rol ve sahiplik kuralları. Her test kendi benzersiz kullanıcılarını kurar.
public class PrescriptionAuthorizationTests : IClassFixture<PrescriptionApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PrescriptionApiFactory _factory;

    public PrescriptionAuthorizationTests(PrescriptionApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private HttpClient Client(string? token = null)
    {
        var client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    private static string PatientToken(string sub) => TestJwt.Create(sub, [HastaneRoles.Patient]);
    private static string DoctorToken(string doctorId) => TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], doctorId);
    private static string AdminToken() => TestJwt.Create(Unique("adm"), [HastaneRoles.Admin]);

    private static CreatePrescriptionDto Body(string patientId, string? doctorId = null) => new()
    {
        AppointmentId = Guid.NewGuid(),
        DoctorId = doctorId,
        PatientId = patientId,
        Notes = "Bol sıvı",
        Items = [new CreatePrescriptionItemDto { MedicineName = "Parol", Dosage = "500 mg", Frequency = "günde 3", DurationDays = 5 }],
    };

    // Reçeteyi doktor kendi token'ıyla yazar (gövdede DoctorId yok): yazar token'daki doctor_id olur.
    private async Task<GetByIdPrescriptionDto> CreateAsDoctorAsync(string doctorId, string patientId)
    {
        var response = await Client(DoctorToken(doctorId)).PostAsJsonAsync("/api/prescriptions", Body(patientId), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GetByIdPrescriptionDto>(Json, Ct))!;
    }

    private static Task<HttpResponseMessage> Update(HttpClient c, Guid id)
        => c.PutAsJsonAsync($"/api/prescriptions/{id}", new UpdatePrescriptionDto
        {
            Id = id,
            Notes = "güncel",
            Items = [new CreatePrescriptionItemDto { MedicineName = "Aspirin", Dosage = "100 mg", Frequency = "günde 1", DurationDays = 3 }],
        }, Json, Ct);

    private static async Task<List<ResultPrescriptionDto>> List(HttpClient c, string query = "")
        => (await c.GetFromJsonAsync<List<ResultPrescriptionDto>>($"/api/prescriptions{query}", Json, Ct))!;

    // --- 401 ---

    public static TheoryData<string, string> ProtectedEndpoints() => new()
    {
        { "GET", "/api/prescriptions" },
        { "GET", "/api/prescriptions/00000000-0000-0000-0000-000000000001" },
        { "POST", "/api/prescriptions" },
        { "PUT", "/api/prescriptions/00000000-0000-0000-0000-000000000001" },
        { "DELETE", "/api/prescriptions/00000000-0000-0000-0000-000000000001" },
    };

    private static HttpRequestMessage Req(string method, string url) => new(new HttpMethod(method), url)
    {
        Content = new StringContent("{}", Encoding.UTF8, "application/json"),
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task NoToken_Returns401(string method, string url)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await Client().SendAsync(Req(method, url), Ct)).StatusCode);

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task InvalidTokens_Return401(string method, string url)
    {
        var tokens = new[]
        {
            TestJwt.CreateExpired("u1", [HastaneRoles.Admin]),
            TestJwt.CreateWithBrokenSignature("u1", [HastaneRoles.Admin]),
            TestJwt.CreateAlgNone("u1", [HastaneRoles.Admin]),
            TestJwt.CreateHs256WithPublicKeyAsSecret("u1", [HastaneRoles.Admin]),
        };
        foreach (var token in tokens)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(token).SendAsync(Req(method, url), Ct)).StatusCode);
    }

    // --- 403: yanlış rol ---

    [Fact]
    public async Task Patient_CannotCreateUpdateOrDelete_Returns403()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");
        var created = await CreateAsDoctorAsync(doctorId, patient);
        var client = Client(PatientToken(patient));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/prescriptions", Body(patient, doctorId), Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Update(client, created.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/prescriptions/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task UserWithoutRole_Gets403_OnReadAndCreate()
    {
        var client = Client(TestJwt.Create(Unique("usr"), []));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/prescriptions", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/prescriptions", Body(Unique("pat"), Unique("doc")), Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Doctor_WithoutDoctorIdClaim_CannotWrite_Returns403()
    {
        var unlinked = Client(TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor]));
        var response = await unlinked.PostAsJsonAsync("/api/prescriptions", Body(Unique("pat")), Json, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Kimlik token'dan (mass assignment) ---

    [Fact]
    public async Task Doctor_Create_BodyDoctorIdIsIgnored_ClaimWins()
    {
        var myDoctorId = Unique("doc");
        var victimDoctorId = Unique("doc");
        var patient = Unique("pat");
        var response = await Client(DoctorToken(myDoctorId)).PostAsJsonAsync(
            "/api/prescriptions", Body(patient, victimDoctorId), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<GetByIdPrescriptionDto>(Json, Ct))!;

        Assert.Equal(myDoctorId, created.DoctorId);
        Assert.Equal(patient, created.PatientId); // hastayı doktor seçer: gövdeden gelir
        // Kurban doktor adına kayıt oluşmadı.
        Assert.Empty(await List(Client(DoctorToken(victimDoctorId))));
    }

    [Fact]
    public async Task Admin_Create_UsesBodyDoctorId_AndRequiresIt()
    {
        var admin = Client(AdminToken());
        var doctorId = Unique("doc");
        var ok = await admin.PostAsJsonAsync("/api/prescriptions", Body(Unique("pat"), doctorId), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(doctorId, (await ok.Content.ReadFromJsonAsync<GetByIdPrescriptionDto>(Json, Ct))!.DoctorId);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync("/api/prescriptions", Body(Unique("pat")), Json, Ct)).StatusCode);
    }

    // --- Liste: kapsam token'dan ---

    [Fact]
    public async Task Patient_List_OnlyOwn_AndQueryPatientIdCannotWidenScope()
    {
        var me = Unique("pat");
        var other = Unique("pat");
        var mine = await CreateAsDoctorAsync(Unique("doc"), me);
        var theirs = await CreateAsDoctorAsync(Unique("doc"), other);
        var client = Client(PatientToken(me));

        Assert.Equal(mine.Id, Assert.Single(await List(client)).Id);
        var attempt = await List(client, $"?patientId={other}"); // IDOR denemesi
        Assert.DoesNotContain(attempt, p => p.Id == theirs.Id);
        Assert.Equal(mine.Id, Assert.Single(attempt).Id);
    }

    [Fact]
    public async Task Doctor_List_OnlyOwn_AndQueryDoctorIdCannotWidenScope()
    {
        var docA = Unique("doc");
        var docB = Unique("doc");
        var forA = await CreateAsDoctorAsync(docA, Unique("pat"));
        var forB = await CreateAsDoctorAsync(docB, Unique("pat"));
        var client = Client(DoctorToken(docA));

        Assert.Equal(forA.Id, Assert.Single(await List(client)).Id);
        var attempt = await List(client, $"?doctorId={docB}");
        Assert.DoesNotContain(attempt, p => p.Id == forB.Id);
        Assert.Equal(forA.Id, Assert.Single(attempt).Id);
    }

    [Fact]
    public async Task Doctor_WithoutDoctorIdClaim_SeesNothing()
    {
        var created = await CreateAsDoctorAsync(Unique("doc"), Unique("pat"));
        var unlinked = Client(TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor]));
        Assert.Empty(await List(unlinked));
        Assert.Equal(HttpStatusCode.NotFound, (await unlinked.GetAsync($"/api/prescriptions/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_List_SeesEverything_WithFilters()
    {
        var doctorId = Unique("doc");
        var created = await CreateAsDoctorAsync(doctorId, Unique("pat"));
        Assert.Equal(created.Id, Assert.Single(await List(Client(AdminToken()), $"?doctorId={doctorId}")).Id);
    }

    // --- Sahiplik ---

    [Fact]
    public async Task GetById_OwnersOk_OthersGet404()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");
        var created = await CreateAsDoctorAsync(doctorId, patient);
        var url = $"/api/prescriptions/{created.Id}";

        Assert.Equal(HttpStatusCode.OK, (await Client(PatientToken(patient)).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(DoctorToken(doctorId)).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(AdminToken()).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(PatientToken(Unique("pat"))).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(DoctorToken(Unique("doc"))).GetAsync(url, Ct)).StatusCode);
    }

    [Fact]
    public async Task Update_OnlyAuthoringDoctorOrAdmin_OtherDoctorGets404()
    {
        var doctorId = Unique("doc");
        var created = await CreateAsDoctorAsync(doctorId, Unique("pat"));

        Assert.Equal(HttpStatusCode.NotFound, (await Update(Client(DoctorToken(Unique("doc"))), created.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Update(Client(DoctorToken(doctorId)), created.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Update(Client(AdminToken()), created.Id)).StatusCode);
    }

    [Fact]
    public async Task Delete_OnlyAuthoringDoctorOrAdmin_OtherDoctorGets404_AndRowSurvives()
    {
        var doctorId = Unique("doc");
        var first = await CreateAsDoctorAsync(doctorId, Unique("pat"));
        var second = await CreateAsDoctorAsync(doctorId, Unique("pat"));

        Assert.Equal(HttpStatusCode.NotFound,
            (await Client(DoctorToken(Unique("doc"))).DeleteAsync($"/api/prescriptions/{first.Id}", Ct)).StatusCode);
        // Başkasının silme girişimi kaydı silmedi.
        Assert.Equal(HttpStatusCode.OK, (await Client(AdminToken()).GetAsync($"/api/prescriptions/{first.Id}", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Client(DoctorToken(doctorId)).DeleteAsync($"/api/prescriptions/{first.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client(AdminToken()).DeleteAsync($"/api/prescriptions/{second.Id}", Ct)).StatusCode);
    }
}
