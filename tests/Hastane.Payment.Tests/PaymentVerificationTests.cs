using System.Net;
using System.Net.Http.Json;
using Hastane.Payment.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Payment.Tests;

// Sunucu tarafı doğrulama: randevu (sahiplik/durum) ve doktor ücreti, kart çekilmeden ÖNCE.
// Appointment ve Doctor servisleri sahte (FakeDownstream); sağlayıcı sayaçlı (CountingPaymentProvider).
[Collection(PaymentCollection.Name)]
public sealed class PaymentVerificationTests : PaymentTestBase, IDisposable
{
    public PaymentVerificationTests(PaymentApiFactory factory) : base(factory)
    {
    }

    public void Dispose() => FakeDownstream.Reset();

    private Task<HttpResponseMessage> PostAs(HttpClient client, CreatePaymentDto dto, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments") { Content = JsonContent.Create(dto, options: Json) };
        request.Headers.Add("Idempotency-Key", key ?? Unique("key"));
        return client.SendAsync(request, Ct);
    }

    private async Task AssertNothingHappenedAsync(CreatePaymentDto dto, int chargesBefore)
    {
        Assert.Equal(chargesBefore, Factory.Provider.ChargeCalls);
        Assert.Equal(0, await WithDbAsync(db => db.Payments.CountAsync(p => p.AppointmentId == dto.AppointmentId, Ct)));
        Assert.Equal(0, await WithDbAsync(db => db.Invoices.CountAsync(i => i.Payment!.AppointmentId == dto.AppointmentId, Ct)));
    }

    // ---------- Randevu ----------

    [Fact]
    public async Task OthersAppointment_Returns404_ProviderNotCalled_NoRows()
    {
        var owner = Unique("pat");
        var dto = NewPayment(patientId: owner);
        var before = Factory.Provider.ChargeCalls;

        // Saldırgan hasta, başkasının randevu kimliğiyle ödeme dener: Appointment ona 404 verir.
        var response = await PostAs(ClientFor(PatientToken(Unique("pat"))), dto);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Randevu bulunamadı", await response.Content.ReadAsStringAsync(Ct));
        await AssertNothingHappenedAsync(dto, before);
    }

    [Fact]
    public async Task NonexistentAppointment_Returns404_ProviderNotCalled()
    {
        var dto = NewPayment();
        dto.AppointmentId = Guid.CreateVersion7(); // hiç kaydedilmedi
        var before = Factory.Provider.ChargeCalls;

        var response = await PostAsync(dto);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingHappenedAsync(dto, before);
    }

    [Fact]
    public async Task CancelledAppointment_Returns409_ProviderNotCalled()
    {
        var dto = NewPayment();
        FakeDownstream.RegisterAppointment(dto.AppointmentId, dto.PatientId!, dto.DoctorId!, "Cancelled");
        var before = Factory.Provider.ChargeCalls;

        var response = await PostAsync(dto);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Bu randevu ödenemez", await response.Content.ReadAsStringAsync(Ct));
        await AssertNothingHappenedAsync(dto, before);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Confirmed")]
    [InlineData("Completed")]
    public async Task PayableStatuses_AreAccepted(string status)
    {
        var dto = NewPayment();
        FakeDownstream.RegisterAppointment(dto.AppointmentId, dto.PatientId!, dto.DoctorId!, status);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(dto)).StatusCode);
    }

    [Fact]
    public async Task BodyDoctorId_DifferentFromAppointment_Returns400_ProviderNotCalled()
    {
        var dto = NewPayment();
        dto.DoctorId = Unique("other-doc");
        // Randevu hâlâ NewPayment'ın doktoruna ait; gövdedeki farklı doktor reddedilir.
        FakeDownstream.RegisterAppointment(dto.AppointmentId, dto.PatientId!, Unique("real-doc"));
        var before = Factory.Provider.ChargeCalls;

        var response = await PostAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingHappenedAsync(dto, before);
    }

    [Fact]
    public async Task BodyDoctorId_Omitted_DoctorComesFromAppointment_AndInvoiceCarriesIt()
    {
        var dto = NewPayment(amount: 275.50m);
        var appointmentDoctor = dto.DoctorId!;
        dto.DoctorId = null;

        var response = await PostAsync(dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await ReadAsync<PaymentResultDto>(response);
        Assert.Equal(appointmentDoctor, result.DoctorId);
        var invoice = await ReadAsync<GetByIdInvoiceDto>(await Client.GetAsync($"/api/invoices/{result.InvoiceId}", Ct));
        Assert.Equal(appointmentDoctor, invoice.DoctorId);
        Assert.Equal(275.50m, invoice.Total);
    }

    [Fact]
    public async Task Admin_PayingForSomeoneElse_PatientComesFromAppointment()
    {
        var patient = Unique("pat");
        var dto = NewPayment(patientId: patient);
        dto.PatientId = null; // admin gövdede hasta vermez

        var response = await PostAs(ClientFor(AdminToken()), dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await ReadAsync<PaymentResultDto>(response);
        Assert.Equal(patient, result.PatientId);
        var invoice = await ReadAsync<GetByIdInvoiceDto>(await Client.GetAsync($"/api/invoices/{result.InvoiceId}", Ct));
        Assert.Equal(patient, invoice.PatientId);
    }

    // ---------- Tutar ----------

    [Theory]
    [InlineData(0.01, "TRY")]    // düşük tutarla ödeme denemesi
    [InlineData(109.99, "TRY")]
    [InlineData(110.01, "TRY")]
    [InlineData(9999, "TRY")]    // yüksek
    [InlineData(110, "USD")]     // doğru sayı, yanlış para birimi
    [InlineData(110, "EUR")]
    public async Task WrongAmountOrCurrency_Returns409_WithServerAmount_ProviderNotCalled(double amount, string currency)
    {
        var dto = NewPayment(amount: 110m); // sunucunun ücreti: 110,00 TRY
        dto.Amount = (decimal)amount;
        dto.Currency = currency;
        var before = Factory.Provider.ChargeCalls;

        var response = await PostAsync(dto);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Tutar güncel değil: 110.00 TRY", await response.Content.ReadAsStringAsync(Ct));
        await AssertNothingHappenedAsync(dto, before);
    }

    [Fact]
    public async Task CorrectAmount_Succeeds_InvoiceUsesServerValues()
    {
        var dto = NewPayment(amount: 220m, currency: "USD");

        var response = await PostAsync(dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await ReadAsync<PaymentResultDto>(response);
        Assert.Equal(220m, result.Amount);
        Assert.Equal("USD", result.Currency);
        var invoice = await ReadAsync<GetByIdInvoiceDto>(await Client.GetAsync($"/api/invoices/{result.InvoiceId}", Ct));
        Assert.Equal(220m, invoice.Total);
        Assert.Equal("USD", invoice.Currency);
        Assert.Equal(dto.DoctorId, invoice.DoctorId);
    }

    [Fact]
    public async Task DoctorWithoutFee_Returns409_ProviderNotCalled()
    {
        var dto = NewPayment();
        FakeDownstream.RegisterDoctor(dto.DoctorId!, new FakeLocation(null, "TRY"));
        var before = Factory.Provider.ChargeCalls;

        var response = await PostAsync(dto);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Doktorun ücreti tanımlı değil", await response.Content.ReadAsStringAsync(Ct));
        await AssertNothingHappenedAsync(dto, before);
    }

    [Fact]
    public async Task DoctorWithNoLocations_Returns409()
    {
        var dto = NewPayment();
        FakeDownstream.RegisterDoctor(dto.DoctorId!);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(dto)).StatusCode);
    }

    [Fact]
    public async Task DoctorMissingInDoctorService_Returns409()
    {
        var dto = NewPayment();
        FakeDownstream.RegisterAppointment(dto.AppointmentId, dto.PatientId!, Unique("ghost-doc"));
        dto.DoctorId = null;
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(dto)).StatusCode);
    }

    // Hesap Doctor'da (DoctorDerived); sahte Doctor aynı kuralı uygular, Payment yalnız minFee/currency'yi karşılaştırır.
    [Fact]
    public async Task MultipleLocations_PaymentComparesAgainstDoctorsMinFee()
    {
        var dto = NewPayment(amount: 300m);
        FakeDownstream.RegisterDoctor(dto.DoctorId!,
            new FakeLocation(500m, "TRY"), new FakeLocation(300m, "TRY"), new FakeLocation(null, "TRY"), new FakeLocation(400m, "TRY"));

        // Daha pahalı bir konumun ücreti kabul edilmez; Doctor'ın minFee'si 300.
        var expensive = NewPayment(amount: 400m, appointmentId: dto.AppointmentId, patientId: dto.PatientId, doctorId: dto.DoctorId);
        FakeDownstream.RegisterDoctor(dto.DoctorId!,
            new FakeLocation(500m, "TRY"), new FakeLocation(300m, "TRY"), new FakeLocation(null, "TRY"), new FakeLocation(400m, "TRY"));
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(expensive)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(dto)).StatusCode);
    }

    [Fact]
    public async Task MixedCurrencies_PaymentUsesDoctorsCurrencyAsIs()
    {
        // Doctor kuralı: ilk ücretli konumun birimi (USD), o birimdeki en düşük ücret (200); TRY 100 sayılmaz.
        var dto = NewPayment(amount: 200m, currency: "USD");
        FakeDownstream.RegisterDoctor(dto.DoctorId!, new FakeLocation(250m, "USD"), new FakeLocation(100m, "TRY"), new FakeLocation(200m, "USD"));

        var wrong = NewPayment(amount: 100m, appointmentId: dto.AppointmentId, patientId: dto.PatientId, doctorId: dto.DoctorId, currency: "TRY");
        FakeDownstream.RegisterDoctor(dto.DoctorId!, new FakeLocation(250m, "USD"), new FakeLocation(100m, "TRY"), new FakeLocation(200m, "USD"));
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(wrong)).StatusCode);

        var response = await PostAsync(dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("USD", (await ReadAsync<PaymentResultDto>(response)).Currency);
    }

    [Fact]
    public async Task ZeroFeeLocationsOnly_Returns409_NotDefined()
    {
        var dto = NewPayment(amount: 0.01m);
        FakeDownstream.RegisterDoctor(dto.DoctorId!, new FakeLocation(0m, "TRY"));
        var before = Factory.Provider.ChargeCalls;

        var response = await PostAsync(dto);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Doktorun ücreti tanımlı değil", await response.Content.ReadAsStringAsync(Ct));
        await AssertNothingHappenedAsync(dto, before);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Downstream403_Returns403Or503_ProviderNotCalled_NoRows(bool appointment)
    {
        var dto = NewPayment();
        if (appointment) FakeDownstream.AppointmentMode = DownstreamMode.Forbidden403;
        else FakeDownstream.DoctorMode = DownstreamMode.Forbidden403;
        var before = Factory.Provider.ChargeCalls;

        var response = await PostAsync(dto);

        // Appointment 403 => 403 aynen; Doctor 403 => servisler arası yetki sorunu, "doğrulanamadı" => 503.
        Assert.Equal(appointment ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await AssertNothingHappenedAsync(dto, before);
    }

    [Fact]
    public async Task SameKey_DoctorIdEmptyThenFilled_Returns409KeyReused()
    {
        var dto = NewPayment();
        var doctorId = dto.DoctorId;
        var key = Unique("keyreuse");
        dto.DoctorId = null;
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(dto, key)).StatusCode);

        dto.DoctorId = doctorId; // gövde değişti => parmak izi farklı
        var response = await PostAsync(dto, key);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Idempotency-Key", await response.Content.ReadAsStringAsync(Ct));
    }

    // ---------- Dayanıklılık ----------

    public static TheoryData<bool, DownstreamMode> Outages()
    {
        var data = new TheoryData<bool, DownstreamMode>();
        foreach (var appointment in new[] { true, false })
        {
            foreach (var mode in new[] { DownstreamMode.Down, DownstreamMode.Timeout, DownstreamMode.Error500, DownstreamMode.BadJson })
                data.Add(appointment, mode);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Outages))]
    public async Task DownstreamOutage_Returns503_ProviderNotCalled_NoRows_AndNoLeak(bool appointmentDown, DownstreamMode mode)
    {
        var dto = NewPayment();
        if (appointmentDown) FakeDownstream.AppointmentMode = mode;
        else FakeDownstream.DoctorMode = mode;
        var before = Factory.Provider.ChargeCalls;
        var key = Unique("outage");

        var response = await PostAsync(dto, key);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("   at ", body); // stack trace yok
        await AssertNothingHappenedAsync(dto, before);
        Assert.Equal(0, await WithDbAsync(db => db.Payments.CountAsync(p => p.IdempotencyKey == key, Ct)));

        // Servis düzelince AYNI anahtarla tekrar deneme başarılı (anahtar tüketilmedi).
        FakeDownstream.Reset();
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(dto, key)).StatusCode);
    }

    // ---------- Authorization iletimi ----------

    [Fact]
    public async Task AuthorizationHeader_IsForwardedVerbatim_ToBothServices()
    {
        var patient = Unique("pat");
        var dto = NewPayment(patientId: patient);
        var token = PatientToken(patient);
        var before = FakeDownstream.Calls.Count;

        var response = await PostAs(ClientFor(token), dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var calls = FakeDownstream.Calls.Skip(before).ToList();
        var appointmentCall = Assert.Single(calls, c => c.Path == $"/api/appointments/{dto.AppointmentId:D}");
        var doctorCall = Assert.Single(calls, c => c.Path == $"/api/doctors/{dto.DoctorId}");
        Assert.Equal("Bearer " + token, appointmentCall.Authorization);
        Assert.Equal("Bearer " + token, doctorCall.Authorization);
    }

    // ---------- Idempotency ----------

    [Fact]
    public async Task CompletedIdempotentReplay_DoesNotCallDownstreamAgain_ReturnsSameResult()
    {
        var dto = NewPayment();
        var key = Unique("replay");
        var first = await PostAsync(dto, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstResult = await ReadAsync<PaymentResultDto>(first);
        var downstreamBefore = FakeDownstream.Calls.Count;
        var chargesBefore = Factory.Provider.ChargeCalls;

        // Randevu bu arada iptal edilse ve servisler kapansa bile: tamamlanmış anahtar ilk sonucu doğrulamasız döndürür.
        FakeDownstream.RegisterAppointment(dto.AppointmentId, dto.PatientId!, dto.DoctorId!, "Cancelled");
        FakeDownstream.AppointmentMode = DownstreamMode.Down;
        FakeDownstream.DoctorMode = DownstreamMode.Down;
        var replay = await PostAsync(dto, key);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstResult.Id, (await ReadAsync<PaymentResultDto>(replay)).Id);
        Assert.Equal(downstreamBefore, FakeDownstream.Calls.Count);
        Assert.Equal(chargesBefore, Factory.Provider.ChargeCalls);
    }

    [Fact]
    public async Task RejectedRequest_DoesNotConsumeKey_CorrectedRetryWithSameKeySucceeds()
    {
        var dto = NewPayment(amount: 110m);
        var key = Unique("fix");
        dto.Amount = 1m;
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(dto, key)).StatusCode);

        dto.Amount = 110m;
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(dto, key)).StatusCode);
    }

    [Fact]
    public async Task DoctorToken_CannotCreate_EvenBeforeAnyDownstreamCall()
    {
        var dto = NewPayment();
        var before = FakeDownstream.Calls.Count;
        var response = await PostAs(ClientFor(TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], dto.DoctorId)), dto);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, FakeDownstream.Calls.Count);
    }
}
