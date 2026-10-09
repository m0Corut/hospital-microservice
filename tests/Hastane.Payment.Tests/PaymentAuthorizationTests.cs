using System.Net;
using System.Net.Http.Json;
using System.Text;
using Hastane.Payment.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Payment.Tests;

// JWT-TASARIM.md §5/§7: kimlik, rol ve sahiplik kuralları. Her test kendi benzersiz kullanıcılarını kurar.
[Collection(PaymentCollection.Name)]
public class PaymentAuthorizationTests : PaymentTestBase
{
    public PaymentAuthorizationTests(PaymentApiFactory factory) : base(factory)
    {
    }

    private Task<HttpResponseMessage> PostAs(HttpClient client, CreatePaymentDto dto)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments") { Content = JsonContent.Create(dto, options: Json) };
        request.Headers.Add("Idempotency-Key", Unique("key"));
        return client.SendAsync(request, Ct);
    }

    // Ödemeyi hasta kendi token'ıyla yapar (gövdede PatientId yok): sahibi token'daki sub olur.
    private async Task<PaymentResultDto> PayAsPatientAsync(string patientSub, string doctorId)
    {
        var dto = NewPayment(doctorId: doctorId);
        dto.PatientId = null;
        var response = await PostAs(ClientFor(PatientToken(patientSub)), dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<PaymentResultDto>(response);
    }

    private static async Task<PagedResult<T>> ListAs<T>(HttpClient c, string url)
    {
        var response = await c.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<PagedResult<T>>(response);
    }

    // --- 401 ---

    public static TheoryData<string, string> ProtectedEndpoints() => new()
    {
        { "GET", "/api/payments" },
        { "GET", "/api/payments/00000000-0000-0000-0000-000000000001" },
        { "POST", "/api/payments" },
        { "POST", "/api/payments/00000000-0000-0000-0000-000000000001/refund" },
        { "GET", "/api/invoices" },
        { "GET", "/api/invoices/00000000-0000-0000-0000-000000000001" },
    };

    private static HttpRequestMessage Req(string method, string url) => new(new HttpMethod(method), url)
    {
        Content = new StringContent("{}", Encoding.UTF8, "application/json"),
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task NoToken_Returns401(string method, string url)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor().SendAsync(Req(method, url), Ct)).StatusCode);

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
            Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(token).SendAsync(Req(method, url), Ct)).StatusCode);
    }

    // --- 403: yanlış rol ---

    [Fact]
    public async Task Doctor_CannotCreatePayment_Returns403()
    {
        var response = await PostAs(ClientFor(DoctorToken(Unique("doc"))), NewPayment());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutRole_Gets403_OnReadAndCreate()
    {
        var client = ClientFor(TestJwt.Create(Unique("usr"), []));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/payments", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/invoices", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAs(client, NewPayment())).StatusCode);
    }

    [Fact]
    public async Task Refund_OnlyAdmin_PatientAndDoctorGet403()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");
        var paid = await PayAsPatientAsync(patient, doctorId);
        var url = $"/api/payments/{paid.Id}/refund";

        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(PatientToken(patient)).PostAsync(url, null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(DoctorToken(doctorId)).PostAsync(url, null, Ct)).StatusCode);
        // Reddedilen iade ödemeyi değiştirmedi.
        Assert.Equal("Paid", (await ReadAsync<PaymentResultDto>(await Client.GetAsync($"/api/payments/{paid.Id}", Ct))).Status);

        Assert.Equal(HttpStatusCode.OK, (await ClientFor(AdminToken()).PostAsync(url, null, Ct)).StatusCode);
    }

    // --- Kimlik token'dan (mass assignment) ---

    [Fact]
    public async Task Patient_Create_BodyPatientIdIsIgnored_TokenSubWins()
    {
        var me = Unique("pat");
        var victim = Unique("pat");
        var dto = NewPayment(patientId: victim);
        var response = await PostAs(ClientFor(PatientToken(me)), dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsync<PaymentResultDto>(response);

        Assert.Equal(me, created.PatientId);
        // Fatura da token'daki hastaya yazıldı; kurban adına kayıt yok.
        var invoice = await ReadAsync<GetByIdInvoiceDto>(await Client.GetAsync($"/api/invoices/{created.InvoiceId}", Ct));
        Assert.Equal(me, invoice.PatientId);
        Assert.Empty((await ListAs<PaymentResultDto>(ClientFor(PatientToken(victim)), "/api/payments")).Items);
    }

    [Fact]
    public async Task Admin_Create_UsesBodyPatientId_AndRequiresIt()
    {
        var admin = ClientFor(AdminToken());
        var patient = Unique("pat");
        var ok = await PostAs(admin, NewPayment(patientId: patient));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(patient, (await ReadAsync<PaymentResultDto>(ok)).PatientId);

        var dto = NewPayment();
        dto.PatientId = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAs(admin, dto)).StatusCode);
    }

    [Fact]
    public async Task IdempotencyKey_ReusedByAnotherPatient_DoesNotLeakOthersPayment()
    {
        var first = Unique("pat");
        var key = Unique("shared-key");
        var dto = NewPayment();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments") { Content = JsonContent.Create(dto, options: Json) };
        request.Headers.Add("Idempotency-Key", key);
        Assert.Equal(HttpStatusCode.Created, (await ClientFor(PatientToken(first)).SendAsync(request, Ct)).StatusCode);

        // Başka bir hasta aynı anahtarı ve aynı gövdeyi dener: özet (hasta token'dan) farklı olduğundan ilk ödeme dönmez.
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/payments") { Content = JsonContent.Create(dto, options: Json) };
        request2.Headers.Add("Idempotency-Key", key);
        var second = await ClientFor(PatientToken(Unique("pat"))).SendAsync(request2, Ct);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // --- Liste: kapsam token'dan, filtre zorlanır ---

    [Fact]
    public async Task Patient_PaymentList_OnlyOwn_AndQueryPatientIdCannotWidenScope()
    {
        var me = Unique("pat");
        var other = Unique("pat");
        var mine = await PayAsPatientAsync(me, Unique("doc"));
        var theirs = await PayAsPatientAsync(other, Unique("doc"));
        var client = ClientFor(PatientToken(me));

        Assert.Equal(mine.Id, Assert.Single((await ListAs<PaymentResultDto>(client, "/api/payments")).Items).Id);
        var attempt = await ListAs<PaymentResultDto>(client, $"/api/payments?patientId={other}"); // IDOR denemesi
        Assert.DoesNotContain(attempt.Items, p => p.Id == theirs.Id);
        Assert.Equal(mine.Id, Assert.Single(attempt.Items).Id);
        Assert.Equal(1, attempt.TotalCount);
    }

    [Fact]
    public async Task Patient_InvoiceList_OnlyOwn_AndQueryPatientIdCannotWidenScope()
    {
        var me = Unique("pat");
        var other = Unique("pat");
        var mine = await PayAsPatientAsync(me, Unique("doc"));
        var theirs = await PayAsPatientAsync(other, Unique("doc"));
        var client = ClientFor(PatientToken(me));

        var attempt = await ListAs<ResultInvoiceDto>(client, $"/api/invoices?patientId={other}");
        Assert.DoesNotContain(attempt.Items, i => i.PaymentId == theirs.Id);
        Assert.Equal(mine.Id, Assert.Single(attempt.Items).PaymentId);
    }

    [Fact]
    public async Task Doctor_Lists_OnlyOwn_AndQueryDoctorIdCannotWidenScope()
    {
        var docA = Unique("doc");
        var docB = Unique("doc");
        var forA = await PayAsPatientAsync(Unique("pat"), docA);
        var forB = await PayAsPatientAsync(Unique("pat"), docB);
        var client = ClientFor(DoctorToken(docA));

        Assert.Equal(forA.Id, Assert.Single((await ListAs<PaymentResultDto>(client, "/api/payments")).Items).Id);
        var attempt = await ListAs<PaymentResultDto>(client, $"/api/payments?doctorId={docB}");
        Assert.DoesNotContain(attempt.Items, p => p.Id == forB.Id);
        var invoices = await ListAs<ResultInvoiceDto>(client, $"/api/invoices?doctorId={docB}");
        Assert.DoesNotContain(invoices.Items, i => i.PaymentId == forB.Id);
        Assert.Equal(forA.Id, Assert.Single(invoices.Items).PaymentId);
    }

    [Fact]
    public async Task Doctor_WithoutDoctorIdClaim_SeesNothing()
    {
        var paid = await PayAsPatientAsync(Unique("pat"), Unique("doc"));
        var unlinked = ClientFor(TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor]));
        Assert.Empty((await ListAs<PaymentResultDto>(unlinked, "/api/payments")).Items);
        Assert.Empty((await ListAs<ResultInvoiceDto>(unlinked, "/api/invoices")).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await unlinked.GetAsync($"/api/payments/{paid.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Admin_List_SeesEverything_WithFilters()
    {
        var doctorId = Unique("doc");
        var paid = await PayAsPatientAsync(Unique("pat"), doctorId);
        var admin = ClientFor(AdminToken());
        Assert.Equal(paid.Id, Assert.Single((await ListAs<PaymentResultDto>(admin, $"/api/payments?doctorId={doctorId}")).Items).Id);
    }

    // --- Sahiplik: başkasının kaynağı 404 ---

    [Fact]
    public async Task GetPayment_OwnersOk_OthersGet404()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");
        var paid = await PayAsPatientAsync(patient, doctorId);
        var url = $"/api/payments/{paid.Id}";

        Assert.Equal(HttpStatusCode.OK, (await ClientFor(PatientToken(patient)).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(DoctorToken(doctorId)).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(AdminToken()).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(PatientToken(Unique("pat"))).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(DoctorToken(Unique("doc"))).GetAsync(url, Ct)).StatusCode);
    }

    [Fact]
    public async Task GetInvoice_OwnersOk_OthersGet404()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");
        var paid = await PayAsPatientAsync(patient, doctorId);
        var url = $"/api/invoices/{paid.InvoiceId}";

        Assert.Equal(HttpStatusCode.OK, (await ClientFor(PatientToken(patient)).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(DoctorToken(doctorId)).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(AdminToken()).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(PatientToken(Unique("pat"))).GetAsync(url, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(DoctorToken(Unique("doc"))).GetAsync(url, Ct)).StatusCode);
    }
}
