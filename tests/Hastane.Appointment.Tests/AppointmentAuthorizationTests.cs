using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Appointment.Tests;

// JWT-TASARIM.md §5/§7: kimlik, rol ve sahiplik kuralları. Her test kendi benzersiz kullanıcılarını kurar.
public class AppointmentAuthorizationTests : IClassFixture<AppointmentApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppointmentApiFactory _factory;

    public AppointmentAuthorizationTests(AppointmentApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
    private static DateTime Future(int days = 5) => DateTime.UtcNow.AddDays(days);

    private HttpClient Client(string? token = null)
    {
        var client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    private static string PatientToken(string sub) => TestJwt.Create(sub, [HastaneRoles.Patient]);
    private static string DoctorToken(string doctorId, string? sub = null) =>
        TestJwt.Create(sub ?? Unique("usr"), [HastaneRoles.Doctor], doctorId);
    private static string AdminToken() => TestJwt.Create(Unique("adm"), [HastaneRoles.Admin]);

    private static CreateAppointmentDto Body(string doctorId, string? patientId = null) => new()
    {
        DoctorId = doctorId,
        PatientId = patientId,
        BranchId = "branch-1",
        AppointmentDate = Future(),
    };

    // Randevuyu hasta kendi token'ıyla açar (gövdede PatientId yok): sahibi token'daki sub olur.
    private async Task<ResultAppointmentDto> CreateAsPatientAsync(string patientSub, string doctorId)
    {
        var response = await Client(PatientToken(patientSub)).PostAsJsonAsync("/api/appointments", Body(doctorId), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;
    }

    private static Task<HttpResponseMessage> Patch(HttpClient c, Guid id, AppointmentStatus status)
        => c.PatchAsJsonAsync($"/api/appointments/{id}/status", new UpdateStatusDto { Status = status }, Json, Ct);

    private static Task<HttpResponseMessage> Reschedule(HttpClient c, Guid id)
        => c.PutAsJsonAsync($"/api/appointments/{id}",
            new UpdateAppointmentDto { Id = id, BranchId = "branch-2", AppointmentDate = Future(30) }, Json, Ct);

    private static async Task<List<ResultAppointmentDto>> List(HttpClient c, string query = "")
        => (await c.GetFromJsonAsync<List<ResultAppointmentDto>>($"/api/appointments{query}", Json, Ct))!;

    // --- 401: token yok / geçersiz ---

    public static TheoryData<string, string> ProtectedEndpoints() => new()
    {
        { "GET", "/api/appointments" },
        { "GET", "/api/appointments/availability?doctorId=x&date=2030-01-07" },
        { "GET", "/api/appointments/00000000-0000-0000-0000-000000000001" },
        { "POST", "/api/appointments" },
        { "PUT", "/api/appointments/00000000-0000-0000-0000-000000000001" },
        { "PATCH", "/api/appointments/00000000-0000-0000-0000-000000000001/status" },
        { "DELETE", "/api/appointments/00000000-0000-0000-0000-000000000001" },
        { "GET", "/api/appointments/00000000-0000-0000-0000-000000000001/detail" },
        { "PUT", "/api/appointments/00000000-0000-0000-0000-000000000001/detail" },
    };

    private static HttpRequestMessage Req(string method, string url) => new(new HttpMethod(method), url)
    {
        Content = new StringContent("{}", Encoding.UTF8, "application/json"),
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task NoToken_Returns401(string method, string url)
    {
        var response = await Client().SendAsync(Req(method, url), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

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
        {
            var response = await Client(token).SendAsync(Req(method, url), Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // --- 403: yanlış rol ---

    [Fact]
    public async Task Doctor_CannotCreate_Returns403()
    {
        var response = await Client(DoctorToken(Unique("doc"))).PostAsJsonAsync(
            "/api/appointments", Body(Unique("doc"), Unique("pat")), Json, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutRole_Gets403_OnReadAndCreate()
    {
        var client = Client(TestJwt.Create(Unique("usr"), []));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/appointments", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/appointments", Body(Unique("doc"), Unique("pat")), Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task OnlyAdmin_CanDelete()
    {
        var doctorId = Unique("doc");
        var patient = Unique("pat");
        var created = await CreateAsPatientAsync(patient, doctorId);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client(PatientToken(patient)).DeleteAsync($"/api/appointments/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client(DoctorToken(doctorId)).DeleteAsync($"/api/appointments/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Client(AdminToken()).DeleteAsync($"/api/appointments/{created.Id}", Ct)).StatusCode);
    }

    // --- Kimlik token'dan (mass assignment) ---

    [Fact]
    public async Task Patient_Create_BodyPatientIdIsIgnored_TokenSubWins()
    {
        var me = Unique("pat");
        var victim = Unique("pat");
        var response = await Client(PatientToken(me)).PostAsJsonAsync(
            "/api/appointments", Body(Unique("doc"), victim), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;

        Assert.Equal(me, created.PatientId);
        // Kurban adına kayıt oluşmadı: kurbanın listesi boş.
        Assert.Empty(await List(Client(PatientToken(victim))));
    }

    [Fact]
    public async Task Patient_Create_WithoutPatientIdInBody_Works()
    {
        var me = Unique("pat");
        var body = $$"""{"doctorId":"{{Unique("doc")}}","branchId":"b","appointmentDate":"{{Future():O}}"}""";
        var response = await Client(PatientToken(me)).PostAsync(
            "/api/appointments", new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(me, (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!.PatientId);
    }

    [Fact]
    public async Task Admin_Create_UsesBodyPatientId_AndRequiresIt()
    {
        var admin = Client(AdminToken());
        var patient = Unique("pat");
        var ok = await admin.PostAsJsonAsync("/api/appointments", Body(Unique("doc"), patient), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(patient, (await ok.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!.PatientId);

        var missing = await admin.PostAsJsonAsync("/api/appointments", Body(Unique("doc")), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    // --- Liste: kapsam token'dan, filtre zorlanır ---

    [Fact]
    public async Task Patient_List_OnlyOwn_AndQueryPatientIdCannotWidenScope()
    {
        var me = Unique("pat");
        var other = Unique("pat");
        var mine = await CreateAsPatientAsync(me, Unique("doc"));
        var theirs = await CreateAsPatientAsync(other, Unique("doc"));
        var client = Client(PatientToken(me));

        Assert.Equal(mine.Id, Assert.Single(await List(client)).Id);
        // IDOR denemesi: başkasının patientId'si sorguda. Filtre token'daki sub ile ezilir.
        var attempt = await List(client, $"?patientId={other}");
        Assert.DoesNotContain(attempt, a => a.Id == theirs.Id);
        Assert.Equal(mine.Id, Assert.Single(attempt).Id);
    }

    [Fact]
    public async Task Doctor_List_OnlyOwn_AndQueryDoctorIdCannotWidenScope()
    {
        var docA = Unique("doc");
        var docB = Unique("doc");
        var forA = await CreateAsPatientAsync(Unique("pat"), docA);
        var forB = await CreateAsPatientAsync(Unique("pat"), docB);
        var client = Client(DoctorToken(docA));

        Assert.Equal(forA.Id, Assert.Single(await List(client)).Id);
        var attempt = await List(client, $"?doctorId={docB}");
        Assert.DoesNotContain(attempt, a => a.Id == forB.Id);
        Assert.Equal(forA.Id, Assert.Single(attempt).Id);
    }

    [Fact]
    public async Task Doctor_WithoutDoctorIdClaim_SeesNothing()
    {
        var doctorId = Unique("doc");
        var created = await CreateAsPatientAsync(Unique("pat"), doctorId);
        var unlinked = Client(TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor])); // doctor_id claim'i yok

        Assert.Empty(await List(unlinked));
        Assert.Empty(await List(unlinked, $"?doctorId={doctorId}"));
        Assert.Equal(HttpStatusCode.NotFound, (await unlinked.GetAsync($"/api/appointments/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_List_SeesEverything_WithFilters()
    {
        var doctorId = Unique("doc");
        var a = await CreateAsPatientAsync(Unique("pat"), doctorId);
        var admin = Client(AdminToken());
        Assert.Equal(a.Id, Assert.Single(await List(admin, $"?doctorId={doctorId}")).Id);
    }

    // --- Sahiplik: başkasının kaynağı 404 ---

    [Fact]
    public async Task GetById_OwnerOk_OthersGet404_AdminOk()
    {
        var me = Unique("pat");
        var doctorId = Unique("doc");
        var created = await CreateAsPatientAsync(me, doctorId);
        var url = $"/api/appointments/{created.Id}";

        Assert.Equal(HttpStatusCode.OK, (await Client(PatientToken(me)).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(DoctorToken(doctorId)).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(AdminToken()).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(PatientToken(Unique("pat"))).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(DoctorToken(Unique("doc"))).GetAsync(url, Ct)).StatusCode);
    }

    [Fact]
    public async Task Reschedule_PatientDoctorAdminOk_OthersGet404()
    {
        var me = Unique("pat");
        var doctorId = Unique("doc");
        var created = await CreateAsPatientAsync(me, doctorId);

        Assert.Equal(HttpStatusCode.NotFound, (await Reschedule(Client(PatientToken(Unique("pat"))), created.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Reschedule(Client(DoctorToken(Unique("doc"))), created.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Reschedule(Client(PatientToken(me)), created.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Reschedule(Client(DoctorToken(doctorId)), created.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Reschedule(Client(AdminToken()), created.Id)).StatusCode);
    }

    // --- PATCH status: alan düzeyinde yetki ---

    [Fact]
    public async Task Patient_CanOnlyCancel_OwnAppointment()
    {
        var me = Unique("pat");
        var created = await CreateAsPatientAsync(me, Unique("doc"));
        var client = Client(PatientToken(me));

        Assert.Equal(HttpStatusCode.Forbidden, (await Patch(client, created.Id, AppointmentStatus.Confirmed)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Patch(client, created.Id, AppointmentStatus.Completed)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Patch(client, created.Id, AppointmentStatus.Pending)).StatusCode);
        // Reddedilen geçişler durumu değiştirmedi.
        var still = await Client(AdminToken()).GetFromJsonAsync<ResultAppointmentDto>($"/api/appointments/{created.Id}", Json, Ct);
        Assert.Equal(AppointmentStatus.Pending, still!.Status);

        var cancel = await Patch(client, created.Id, AppointmentStatus.Cancelled);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(AppointmentStatus.Cancelled, (await cancel.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!.Status);
    }

    [Fact]
    public async Task Patient_CannotChangeStatus_OfSomeoneElsesAppointment_Gets404()
    {
        var created = await CreateAsPatientAsync(Unique("pat"), Unique("doc"));
        var stranger = Client(PatientToken(Unique("pat")));
        // 403 değil 404: başkasının randevusunun var olduğu bile sızmaz.
        Assert.Equal(HttpStatusCode.NotFound, (await Patch(stranger, created.Id, AppointmentStatus.Cancelled)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Patch(stranger, created.Id, AppointmentStatus.Confirmed)).StatusCode);
    }

    [Fact]
    public async Task Doctor_CanConfirmAndComplete_OwnOnly_AndStateMachineStillApplies()
    {
        var doctorId = Unique("doc");
        var created = await CreateAsPatientAsync(Unique("pat"), doctorId);
        var own = Client(DoctorToken(doctorId));
        var other = Client(DoctorToken(Unique("doc")));

        Assert.Equal(HttpStatusCode.NotFound, (await Patch(other, created.Id, AppointmentStatus.Confirmed)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Patch(own, created.Id, AppointmentStatus.Pending)).StatusCode);
        // Durum makinesi (409) aynen geçerli: Pending -> Completed yasak.
        Assert.Equal(HttpStatusCode.Conflict, (await Patch(own, created.Id, AppointmentStatus.Completed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Patch(own, created.Id, AppointmentStatus.Confirmed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Patch(own, created.Id, AppointmentStatus.Completed)).StatusCode);
    }

    [Fact]
    public async Task Doctor_CanCancel_OwnAppointment()
    {
        var doctorId = Unique("doc");
        var created = await CreateAsPatientAsync(Unique("pat"), doctorId);
        Assert.Equal(HttpStatusCode.OK, (await Patch(Client(DoctorToken(doctorId)), created.Id, AppointmentStatus.Cancelled)).StatusCode);
    }

    [Fact]
    public async Task Admin_CanSetAnyAllowedTransition()
    {
        var created = await CreateAsPatientAsync(Unique("pat"), Unique("doc"));
        var admin = Client(AdminToken());
        Assert.Equal(HttpStatusCode.OK, (await Patch(admin, created.Id, AppointmentStatus.Confirmed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Patch(admin, created.Id, AppointmentStatus.Completed)).StatusCode);
    }

    // --- Detay ---

    [Fact]
    public async Task Detail_DoctorOwnerWrites_PatientOwnerReads_StrangersGet404_PatientWriteIs403()
    {
        var me = Unique("pat");
        var doctorId = Unique("doc");
        var created = await CreateAsPatientAsync(me, doctorId);
        var url = $"/api/appointments/{created.Id}/detail";
        var body = new UpsertAppointmentDetailDto { Complaint = "Öksürük", Diagnosis = "Nezle" };

        // Hasta yazamaz (rol yetmiyor: 403), başka doktor göremez/yazamaz (404).
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(PatientToken(me)).PutAsJsonAsync(url, body, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(DoctorToken(Unique("doc"))).PutAsJsonAsync(url, body, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Client(DoctorToken(doctorId)).PutAsJsonAsync(url, body, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(AdminToken()).PutAsJsonAsync(url, body, Json, Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Client(PatientToken(me)).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(DoctorToken(doctorId)).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(AdminToken()).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(PatientToken(Unique("pat"))).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(DoctorToken(Unique("doc"))).GetAsync(url, Ct)).StatusCode);
    }
}
