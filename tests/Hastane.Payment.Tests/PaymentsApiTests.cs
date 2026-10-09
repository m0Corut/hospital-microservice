using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Hastane.Payment.Api.Dtos;
using Hastane.Payment.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Payment.Tests;

[Collection(PaymentCollection.Name)]
public class PaymentsApiTests : PaymentTestBase
{
    public PaymentsApiTests(PaymentApiFactory factory) : base(factory)
    {
    }

    private async Task<GetByIdInvoiceDto> GetInvoiceAsync(Guid invoiceId)
    {
        var response = await Client.GetAsync($"/api/invoices/{invoiceId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<GetByIdInvoiceDto>(response);
    }

    // ---------- Başarılı ödeme + fatura ----------

    [Fact]
    public async Task Create_Success_WritesPaymentInvoiceAndLine_AndAmountEqualsTotal()
    {
        var dto = NewPayment(amount: 110.00m);
        var response = await PostAsync(dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var result = await ReadAsync<PaymentResultDto>(response);
        Assert.Equal("Paid", result.Status);
        Assert.Equal(110.00m, result.Amount);
        Assert.Equal("TRY", result.Currency);
        Assert.Equal("Visa", result.CardBrand);
        Assert.Equal("4242", result.CardLast4);
        Assert.NotNull(result.PaidAt);
        Assert.NotNull(result.InvoiceId);
        Assert.Matches(new Regex(@"^INV-\d{4}-\d{6}$"), result.InvoiceNumber);

        var invoice = await GetInvoiceAsync(result.InvoiceId!.Value);
        Assert.Equal(result.Id, invoice.PaymentId);
        Assert.Equal(100.00m, invoice.Subtotal);
        Assert.Equal(10.00m, invoice.TaxAmount);
        Assert.Equal(10m, invoice.TaxPercent);
        Assert.Equal(110.00m, invoice.Total);
        Assert.Equal(result.Amount, invoice.Total);
        var line = Assert.Single(invoice.Lines);
        Assert.Equal("Kardiyoloji muayenesi", line.Description);
        Assert.Equal(1, line.Quantity);
        Assert.Equal(100.00m, line.UnitPrice);
        Assert.Equal(100.00m, line.LineTotal);

        // Veritabanında da tek ödeme + tek fatura + tek kalem.
        var counts = await WithDbAsync(async db => (
            await db.Payments.CountAsync(p => p.PatientId == dto.PatientId, Ct),
            await db.Invoices.CountAsync(i => i.PatientId == dto.PatientId, Ct),
            await db.InvoiceLines.CountAsync(l => l.Invoice!.PatientId == dto.PatientId, Ct)));
        Assert.Equal((1, 1, 1), counts);
    }

    [Fact]
    public async Task Create_WithRoundingAmount_TotalStillEqualsAmount()
    {
        var result = await PayOkAsync(NewPayment(amount: 100.00m));
        var invoice = await GetInvoiceAsync(result.InvoiceId!.Value);

        Assert.Equal(90.91m, invoice.Subtotal); // 100 / 1.10 = 90.909... -> 90.91
        Assert.Equal(9.09m, invoice.TaxAmount);
        Assert.Equal(100.00m, invoice.Total);
        Assert.Equal(result.Amount, invoice.Total);
    }

    [Fact]
    public async Task Create_DescriptionMissing_UsesDefaultLineText()
    {
        var dto = NewPayment();
        dto.Description = null;
        var result = await PayOkAsync(dto);
        var invoice = await GetInvoiceAsync(result.InvoiceId!.Value);
        Assert.Equal("Muayene ve hizmet bedeli", Assert.Single(invoice.Lines).Description);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("1000000")]
    public async Task Create_AmountBoundaries_Accepted(string amount)
        => Assert.Equal("Paid", (await PayOkAsync(NewPayment(amount: decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)))).Status);

    [Fact]
    public async Task Create_OtherAllowedCurrencies_Accepted()
    {
        Assert.Equal("USD", (await PayOkAsync(NewPayment(currency: "USD"))).Currency);
        Assert.Equal("EUR", (await PayOkAsync(NewPayment(currency: "EUR"))).Currency);
    }

    // ---------- Sağlayıcı reddi ----------

    [Theory]
    [InlineData(Declined, "Kart reddedildi")]
    [InlineData(NoFunds, "Yetersiz bakiye")]
    public async Task Create_DeclinedCard_Returns402_SavesFailedPayment_NoInvoice(string card, string reason)
    {
        var dto = NewPayment(card: card);
        var response = await PostAsync(dto);

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var result = await ReadAsync<PaymentResultDto>(response);
        Assert.Equal("Failed", result.Status);
        Assert.Equal(reason, result.FailureReason);
        Assert.Null(result.InvoiceId);
        Assert.Null(result.PaidAt);
        Assert.Equal("Visa", result.CardBrand);
        Assert.Equal(card[^4..], result.CardLast4);

        // Denetim izi: Failed satırı var, fatura yok.
        var (payments, invoices) = await WithDbAsync(async db => (
            await db.Payments.CountAsync(p => p.Id == result.Id && p.Status == PaymentStatus.Failed, Ct),
            await db.Invoices.CountAsync(i => i.PaymentId == result.Id, Ct)));
        Assert.Equal(1, payments);
        Assert.Equal(0, invoices);
    }

    [Fact]
    public async Task Create_AfterDecline_SameAppointmentCanBePaidWithNewKey()
    {
        var appointmentId = Guid.CreateVersion7();
        var failed = await PostAsync(NewPayment(card: Declined, appointmentId: appointmentId));
        Assert.Equal(HttpStatusCode.PaymentRequired, failed.StatusCode);

        var ok = await PostAsync(NewPayment(appointmentId: appointmentId));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    // ---------- Doğrulama (400, kayıt oluşmaz) ----------

    public static TheoryData<string, Action<CreatePaymentDto>> InvalidRequests => new()
    {
        { "luhn", d => d.Card.Number = "4242424242424241" },
        { "kart-kisa", d => d.Card.Number = "42424242" },
        { "kart-harf", d => d.Card.Number = "4242abcd42424242" },
        { "skt-gecmis", d => { d.Card.ExpMonth = 1; d.Card.ExpYear = DateTime.UtcNow.Year - 1; } },
        { "ay-13", d => d.Card.ExpMonth = 13 },
        { "cvv-2-hane", d => d.Card.Cvv = "12" },
        { "cvv-5-hane", d => d.Card.Cvv = "12345" },
        { "cvv-harf", d => d.Card.Cvv = "12a" },
        { "sahip-bos", d => d.Card.HolderName = "" },
        { "tutar-0", d => d.Amount = 0m },
        { "tutar-eksi", d => d.Amount = -5m },
        { "tutar-ust-sinir", d => d.Amount = 1_000_000.01m },
        { "tutar-3-ondalik", d => d.Amount = 10.123m },
        { "para-birimi-gbp", d => d.Currency = "GBP" },
        { "para-birimi-kucuk", d => d.Currency = "try" },
        { "para-birimi-uzun", d => d.Currency = "TRYY" },
        { "para-birimi-bos", d => d.Currency = "" },
        { "aciklama-201", d => d.Description = new string('x', 201) },
        { "randevu-bos", d => d.AppointmentId = Guid.Empty },
        { "hasta-65", d => d.PatientId = new string('p', 65) },
        { "doktor-bos", d => d.DoctorId = "" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_InvalidRequest_Returns400_AndWritesNothing(string _, Action<CreatePaymentDto> mutate)
    {
        var dto = NewPayment();
        mutate(dto);

        var response = await PostAsync(dto);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(VisaOk, body);
        Assert.DoesNotContain("4242424242424241", body);

        var count = await WithDbAsync(db => db.Payments.CountAsync(p => p.PatientId == dto.PatientId, Ct));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Create_MissingIdempotencyKey_Returns400()
    {
        var dto = NewPayment();
        var response = await PostWithoutKeyAsync(dto);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.Payments.CountAsync(p => p.PatientId == dto.PatientId, Ct)));
    }

    [Fact]
    public async Task Create_TooLongIdempotencyKey_Returns400()
        => Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(NewPayment(), new string('k', 101))).StatusCode);

    [Fact]
    public async Task Create_EmptyBody_Returns400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments") { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", Unique("k"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.SendAsync(request, Ct)).StatusCode);
    }

    // ---------- Idempotency ----------

    [Fact]
    public async Task Idempotency_SameKeySameBody_ReturnsFirstResult_ProviderCalledOnce()
    {
        var dto = NewPayment();
        var key = Unique("idem");
        var before = Factory.Provider.ChargeCalls;

        var first = await PostAsync(dto, key);
        var second = await PostAsync(dto, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var a = await ReadAsync<PaymentResultDto>(first);
        var b = await ReadAsync<PaymentResultDto>(second);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.InvoiceNumber, b.InvoiceNumber);
        Assert.Equal(before + 1, Factory.Provider.ChargeCalls);
        Assert.Equal(1, await WithDbAsync(db => db.Payments.CountAsync(p => p.IdempotencyKey == key, Ct)));
    }

    [Fact]
    public async Task Idempotency_SameKeyAfterDecline_Replays402_WithoutCallingProviderAgain()
    {
        var dto = NewPayment(card: Declined);
        var key = Unique("idem");
        var before = Factory.Provider.ChargeCalls;

        var first = await PostAsync(dto, key);
        var second = await PostAsync(dto, key);

        Assert.Equal(HttpStatusCode.PaymentRequired, first.StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, second.StatusCode);
        Assert.Equal((await ReadAsync<PaymentResultDto>(first)).Id, (await ReadAsync<PaymentResultDto>(second)).Id);
        Assert.Equal(before + 1, Factory.Provider.ChargeCalls);
    }

    [Fact]
    public async Task Idempotency_SameKeyDifferentBody_Returns409()
    {
        var dto = NewPayment(amount: 110m);
        var key = Unique("idem");
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(dto, key)).StatusCode);

        var otherAmount = NewPayment(amount: 220m, appointmentId: dto.AppointmentId, patientId: dto.PatientId, doctorId: dto.DoctorId);
        var otherAppointment = NewPayment(amount: 110m, patientId: dto.PatientId, doctorId: dto.DoctorId);
        var otherPatient = NewPayment(amount: 110m, appointmentId: dto.AppointmentId, doctorId: dto.DoctorId);

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(otherAmount, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(otherAppointment, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(otherPatient, key)).StatusCode);
    }

    // ---------- Bir randevu = bir Paid ödeme ----------

    [Fact]
    public async Task SecondPaid_SameAppointment_DifferentKey_Returns409()
    {
        var appointmentId = Guid.CreateVersion7();
        await PayOkAsync(NewPayment(appointmentId: appointmentId));
        var before = Factory.Provider.ChargeCalls;

        var second = await PostAsync(NewPayment(appointmentId: appointmentId));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(before, Factory.Provider.ChargeCalls); // servis kontrolü sağlayıcıya gitmeden durdurdu
    }

    [Fact]
    public async Task AfterRefund_SameAppointment_CanBePaidAgain()
    {
        var appointmentId = Guid.CreateVersion7();
        var first = await PayOkAsync(NewPayment(appointmentId: appointmentId));
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync($"/api/payments/{first.Id}/refund", null, Ct)).StatusCode);

        var again = await PostAsync(NewPayment(appointmentId: appointmentId));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);

        var paidCount = await WithDbAsync(db => db.Payments.CountAsync(p => p.AppointmentId == appointmentId && p.Status == PaymentStatus.Paid, Ct));
        Assert.Equal(1, paidCount);
    }

    [Fact]
    public async Task FilteredUniqueIndex_RejectsSecondPaidRow_EvenWhenServiceCheckIsBypassed()
    {
        var appointmentId = Guid.CreateVersion7();
        await PayOkAsync(NewPayment(appointmentId: appointmentId));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => WithDbAsync(async db =>
        {
            db.Payments.Add(new Api.Entities.Payment
            {
                Id = Guid.CreateVersion7(),
                AppointmentId = appointmentId,
                PatientId = "p",
                DoctorId = "d",
                Amount = 1m,
                Currency = "TRY",
                Status = PaymentStatus.Paid,
                IdempotencyKey = Unique("raw"),
                RequestHash = new string('a', 64),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
            return 0;
        }));
        Assert.IsType<Microsoft.Data.SqlClient.SqlException>(ex.InnerException);
    }

    // ---------- İade durum makinesi ----------

    [Fact]
    public async Task Refund_Paid_BecomesRefunded_InvoiceIsKept()
    {
        var paid = await PayOkAsync();
        var before = Factory.Provider.RefundCalls;

        var response = await Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refunded = await ReadAsync<PaymentResultDto>(response);
        Assert.Equal("Refunded", refunded.Status);
        Assert.NotNull(refunded.RefundedAt);
        Assert.Equal(paid.InvoiceId, refunded.InvoiceId);
        Assert.Equal(before + 1, Factory.Provider.RefundCalls);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync($"/api/invoices/{paid.InvoiceId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Refund_AlreadyRefunded_Returns409()
    {
        var paid = await PayOkAsync();
        await Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Refund_Failed_Returns409()
    {
        var response = await PostAsync(NewPayment(card: Declined));
        var failed = await ReadAsync<PaymentResultDto>(response);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync($"/api/payments/{failed.Id}/refund", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Refund_Pending_Returns409()
    {
        var id = Guid.CreateVersion7();
        await WithDbAsync(async db =>
        {
            db.Payments.Add(new Api.Entities.Payment
            {
                Id = id,
                AppointmentId = Guid.CreateVersion7(),
                PatientId = "p",
                DoctorId = "d",
                Amount = 1m,
                Currency = "TRY",
                Status = PaymentStatus.Pending,
                IdempotencyKey = Unique("pending"),
                RequestHash = new string('b', 64),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync($"/api/payments/{id}/refund", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Refund_Unknown_Returns404()
        => Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsync($"/api/payments/{Guid.NewGuid()}/refund", null, Ct)).StatusCode);

    // ---------- Okuma uçları ----------

    [Fact]
    public async Task GetById_ReturnsPayment_AndMalformedOrUnknownIdIs404()
    {
        var paid = await PayOkAsync();
        var ok = await Client.GetAsync($"/api/payments/{paid.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(paid.Id, (await ReadAsync<PaymentResultDto>(ok)).Id);

        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/payments/{Guid.NewGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/payments/abc", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsync("/api/payments/abc/refund", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/invoices/abc", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/invoices/{Guid.NewGuid()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task List_PagingFiltersAndOrder()
    {
        var patient = Unique("pat");
        var doctorA = Unique("docA");
        var doctorB = Unique("docB");
        var first = await PayOkAsync(NewPayment(patientId: patient, doctorId: doctorA));
        var second = await PayOkAsync(NewPayment(patientId: patient, doctorId: doctorB));
        var failedResponse = await PostAsync(NewPayment(card: Declined, patientId: patient, doctorId: doctorA));
        var failed = await ReadAsync<PaymentResultDto>(failedResponse);

        var all = await ListPaymentsAsync($"patientId={patient}");
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(1, all.Page);
        Assert.Equal(10, all.PageSize);
        Assert.Equal([failed.Id, second.Id, first.Id], all.Items.Select(i => i.Id)); // CreatedAt azalan

        var page1 = await ListPaymentsAsync($"patientId={patient}&page=1&pageSize=2");
        var page2 = await ListPaymentsAsync($"patientId={patient}&page=2&pageSize=2");
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(first.Id, page2.Items[0].Id);

        Assert.Equal(2, (await ListPaymentsAsync($"patientId={patient}&doctorId={doctorA}")).TotalCount);
        Assert.Equal(1, (await ListPaymentsAsync($"patientId={patient}&status=Failed")).TotalCount);
        Assert.Equal(2, (await ListPaymentsAsync($"patientId={patient}&status=Paid")).TotalCount);
        Assert.Equal(1, (await ListPaymentsAsync($"appointmentId={first.AppointmentId}")).TotalCount);
        Assert.Equal(0, (await ListPaymentsAsync($"patientId={patient}&status=Refunded")).TotalCount);
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=51")]
    [InlineData("page=0")]
    [InlineData("status=Bogus")]
    public async Task List_InvalidPaging_Returns400(string query)
        => Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/payments?" + query, Ct)).StatusCode);

    [Fact]
    public async Task Invoices_ListFilterOrderAndDetail()
    {
        var patient = Unique("pat");
        var a = await PayOkAsync(NewPayment(patientId: patient, amount: 110m));
        var b = await PayOkAsync(NewPayment(patientId: patient, amount: 220m));

        var response = await Client.GetAsync($"/api/invoices?patientId={patient}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadAsync<PagedResult<ResultInvoiceDto>>(response);
        Assert.Equal(2, list.TotalCount);
        Assert.Equal([b.InvoiceId, a.InvoiceId], list.Items.Select(i => (Guid?)i.Id)); // IssuedAt azalan
        Assert.Equal(220m, list.Items[0].Total);

        Assert.Equal(0, (await ReadAsync<PagedResult<ResultInvoiceDto>>(
            await Client.GetAsync($"/api/invoices?patientId={patient}&doctorId=yok", Ct))).TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/invoices?pageSize=51", Ct)).StatusCode);
    }

    // ---------- Fatura numarası ----------

    [Fact]
    public async Task InvoiceNumbers_AreSequentialFormat_AndUnique()
    {
        var one = await PayOkAsync();
        var two = await PayOkAsync();
        var year = DateTime.UtcNow.Year;
        Assert.Matches(new Regex($@"^INV-{year}-\d{{6}}$"), one.InvoiceNumber);
        Assert.True(string.CompareOrdinal(two.InvoiceNumber, one.InvoiceNumber) > 0);
    }
}
