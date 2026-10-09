using System.Diagnostics;
using System.Net;
using Hastane.Payment.Api.Dtos;
using Hastane.Payment.Api.Entities;
using Hastane.Payment.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Payment.Tests;

// Para güvenliği senaryoları: sağlayıcı/kayıt hataları, telafi, iade yarışı, bayat Pending, istemci iptali.
[Collection(PaymentCollection.Name)]
public class PaymentSafetyTests : PaymentTestBase
{
    public PaymentSafetyTests(PaymentApiFactory factory) : base(factory)
    {
    }

    private Task<Api.Entities.Payment> LoadAsync(Guid id)
        => WithDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.Id == id, Ct));

    // Değer int'tir (kullanıcı girdisi değil); RESTART WITH parametre kabul etmez.
#pragma warning disable EF1003
    private Task<int> RestartSequenceAsync(int value)
        => WithDbAsync(db => db.Database.ExecuteSqlRawAsync("ALTER SEQUENCE InvoiceNumberSeq RESTART WITH " + value, Ct));
#pragma warning restore EF1003

    // ---------- Idempotency-Key biçimi ----------

    [Theory]
    [InlineData("1")]
    [InlineData("kisa-anahtar")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 109
    [InlineData("gecersiz karakter!!! 123456")]
    public async Task IdempotencyKey_BadFormat_Returns400_AndWritesNothing(string key)
    {
        var dto = NewPayment();
        var response = await PostAsync(dto, key);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.Payments.CountAsync(p => p.PatientId == dto.PatientId, Ct)));
    }

    [Fact]
    public async Task IdempotencyKey_Uuid_IsAccepted()
        => Assert.Equal(HttpStatusCode.Created, (await PostAsync(NewPayment(), Guid.NewGuid().ToString())).StatusCode);

    // ---------- Sağlayıcı charge istisnası ----------

    [Fact]
    public async Task ChargeException_Returns502_SavesFailed_NoInvoice_AndSameKeyReplaysSameResult()
    {
        var dto = NewPayment();
        var key = Unique("charge-ex");
        var before = Factory.Provider.ChargeCalls;
        try
        {
            Factory.Provider.ChargeException = new TimeoutException("sağlayıcı zaman aşımı (test)");

            var first = await PostAsync(dto, key);
            Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);

            var row = await WithDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.IdempotencyKey == key, Ct));
            Assert.Equal(PaymentStatus.Failed, row.Status); // Pending kalmadı
            Assert.Equal(PaymentFailureKind.ProviderError, row.FailureKind);
            Assert.Equal(0, await WithDbAsync(db => db.Invoices.CountAsync(i => i.PaymentId == row.Id, Ct)));

            Factory.Provider.Reset(); // sağlayıcı düzelse bile aynı anahtar ilk sonucu döndürür
            var second = await PostAsync(dto, key);
            Assert.Equal(HttpStatusCode.BadGateway, second.StatusCode);
            Assert.Equal(before + 1, Factory.Provider.ChargeCalls);
        }
        finally
        {
            Factory.Provider.Reset();
        }

        // Yeni anahtarla aynı randevu ödenebilir (Failed engellemez).
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(NewPayment(appointmentId: dto.AppointmentId))).StatusCode);
    }

    // ---------- İade: ret / istisna / yarış ----------

    [Theory]
    [InlineData(RefundMode.Reject, HttpStatusCode.Conflict)]
    [InlineData(RefundMode.Throw, HttpStatusCode.BadGateway)]
    public async Task RefundFailure_PaymentStaysPaid_IndexStillBlocksNewPayment_AndRefundCanBeRetried(RefundMode mode, HttpStatusCode expected)
    {
        var appointmentId = Guid.CreateVersion7();
        var paid = await PayOkAsync(NewPayment(appointmentId: appointmentId));
        try
        {
            Factory.Provider.RefundMode = mode;
            var response = await Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct);
            Assert.Equal(expected, response.StatusCode);
        }
        finally
        {
            Factory.Provider.Reset();
        }

        var row = await LoadAsync(paid.Id);
        Assert.Equal(PaymentStatus.Paid, row.Status);
        Assert.Null(row.RefundRequestedAt); // işaret geri alındı
        Assert.Null(row.RefundedAt);

        // Aynı randevuya yeni ödeme hâlâ 409 (iade süresince ve sonrasında Paid kaldığı için indeks korur).
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(NewPayment(appointmentId: appointmentId))).StatusCode);

        // Sağlayıcı düzelince iade yeniden denenebilir.
        var retry = await Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("Refunded", (await ReadAsync<PaymentResultDto>(retry)).Status);
    }

    [Fact]
    public async Task Refund_InProgress_StatusStaysPaid_RefundedAtHidden_NewPaymentBlocked_SecondRefundConflicts()
    {
        var appointmentId = Guid.CreateVersion7();
        var paid = await PayOkAsync(NewPayment(appointmentId: appointmentId));
        HttpResponseMessage? duringNewPayment = null, duringSecondRefund = null;
        PaymentResultDto? duringGet = null;
        try
        {
            // Sağlayıcı iadeyi geciktirirken (işaret alınmış, Status hâlâ Paid) araya girenler.
            Factory.Provider.RefundMode = RefundMode.Succeed;
            Factory.Provider.RefundDelay = TimeSpan.FromMilliseconds(700);
            var refundTask = Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct);
            await Task.Delay(250, Ct);

            duringNewPayment = await PostAsync(NewPayment(appointmentId: appointmentId));
            duringSecondRefund = await Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct);
            duringGet = await ReadAsync<PaymentResultDto>(await Client.GetAsync($"/api/payments/{paid.Id}", Ct));

            Assert.Equal(HttpStatusCode.OK, (await refundTask).StatusCode);
        }
        finally
        {
            Factory.Provider.Reset();
        }

        Assert.Equal(HttpStatusCode.Conflict, duringNewPayment!.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duringSecondRefund!.StatusCode);
        Assert.Equal("Paid", duringGet!.Status);
        Assert.Null(duringGet.RefundedAt); // tutarlı ara durum: iade bitmeden RefundedAt görünmez
        Assert.Equal("Refunded", (await ReadAsync<PaymentResultDto>(await Client.GetAsync($"/api/payments/{paid.Id}", Ct))).Status);
    }

    // ---------- Kayıt hatası: para çekildi ama fatura/ödeme yazılamadı ----------

    [Fact]
    public async Task RecordingFailure_AfterCharge_RefundsCompensates_MarksFailed_Returns502_AndReplaysSame()
    {
        // 1 numaralı fatura zaten var (ilk ödeme). Sayacı 1'e geri alırsak sıradaki fatura numarası ÇAKIŞIR ->
        // SaveChanges tekil indeks hatası verir (Paid indeksi değil): "kayıt yazılamadı" yolu.
        await PayOkAsync();
        var current = await WithDbAsync(db => db.Database
            .SqlQueryRaw<int>("SELECT CAST(current_value AS int) AS [Value] FROM sys.sequences WHERE name = 'InvoiceNumberSeq'")
            .SingleAsync(Ct));

        var dto = NewPayment();
        var key = Unique("record-fail");
        var refundsBefore = Factory.Provider.RefundCalls;
        HttpResponseMessage first, second;
        try
        {
            await RestartSequenceAsync(1);
            first = await PostAsync(dto, key);
            second = await PostAsync(dto, key);
        }
        finally
        {
            await RestartSequenceAsync(current + 10);
        }

        Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, second.StatusCode); // Pending kalmadı, replay aynı sonuç
        Assert.Equal(refundsBefore + 1, Factory.Provider.RefundCalls); // telafi iadesi tam bir kez

        var row = await WithDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.IdempotencyKey == key, Ct));
        Assert.Equal(PaymentStatus.Failed, row.Status);
        Assert.Equal(PaymentFailureKind.RecordingFailed, row.FailureKind);
        Assert.Contains("ücret iade edildi", row.FailureReason);
        Assert.Equal(0, await WithDbAsync(db => db.Invoices.CountAsync(i => i.PaymentId == row.Id, Ct)));

        // Randevu ödenmemiş sayılır; yeni anahtarla ödenebilir.
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(NewPayment(appointmentId: dto.AppointmentId))).StatusCode);
    }

    [Fact]
    public async Task RecordingFailure_AndCompensationRefundAlsoFails_StillMarksFailedWithManualReviewReason()
    {
        await PayOkAsync();
        var current = await WithDbAsync(db => db.Database
            .SqlQueryRaw<int>("SELECT CAST(current_value AS int) AS [Value] FROM sys.sequences WHERE name = 'InvoiceNumberSeq'")
            .SingleAsync(Ct));
        var dto = NewPayment();
        var key = Unique("record-fail2");
        var logsBefore = Factory.Logs.Entries.Count;
        try
        {
            await RestartSequenceAsync(1);
            Factory.Provider.RefundMode = RefundMode.Throw;
            Assert.Equal(HttpStatusCode.BadGateway, (await PostAsync(dto, key)).StatusCode);
        }
        finally
        {
            Factory.Provider.Reset();
            await RestartSequenceAsync(current + 10);
        }

        var row = await WithDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.IdempotencyKey == key, Ct));
        Assert.Equal(PaymentStatus.Failed, row.Status);
        Assert.Contains("elle incelenmeli", row.FailureReason);
        Assert.Contains("mock_", row.ProviderReference); // referans korunur ki elle iade yapılabilsin
        Assert.Contains(Factory.Logs.Entries.Skip(logsBefore), e => e.StartsWith("[Critical]") && e.Contains(row.Id.ToString()) && e.Contains(row.ProviderReference!));
    }

    // ---------- Telafi edilen mükerrer satırın replay'i ilk cevapla tutarlı (409) ----------

    [Fact]
    public async Task CompensatedDuplicate_Replay_Returns409_LikeTheFirstResponse()
    {
        var appointmentId = Guid.CreateVersion7();
        var dtos = Enumerable.Range(0, 6).Select(_ => NewPayment(appointmentId: appointmentId)).ToList();
        var keys = dtos.Select(_ => Unique("dup")).ToList();

        var responses = await Task.WhenAll(dtos.Select((d, i) => PostAsync(d, keys[i])));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));

        var duplicateKeys = await WithDbAsync(db => db.Payments.AsNoTracking()
            .Where(p => p.AppointmentId == appointmentId && p.FailureKind == PaymentFailureKind.Duplicate)
            .Select(p => p.IdempotencyKey).ToListAsync(Ct));
        Assert.NotEmpty(duplicateKeys); // yarış gerçekten oluştu

        foreach (var duplicateKey in duplicateKeys)
        {
            var index = keys.IndexOf(duplicateKey);
            var replay = await PostAsync(dtos[index], duplicateKey);
            Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        }
    }

    // ---------- İstemci iptali ----------

    [Fact]
    public async Task ClientCancellationDuringCharge_PaymentStillCompletesConsistently()
    {
        var dto = NewPayment();
        var key = Unique("cancel");
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        using var cts = new CancellationTokenSource();
        try
        {
            // İstemci, sağlayıcı çağrısı başlar başlamaz bağlantıyı keser.
            Factory.Provider.OnChargeStarted = cts.Cancel;
            var result = await service.CreateAsync(key, dto, cts.Token);

            Assert.True(cts.IsCancellationRequested);
            Assert.Equal(OperationStatus.Created, result.Status);
        }
        finally
        {
            Factory.Provider.Reset();
        }

        var row = await WithDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.IdempotencyKey == key, Ct));
        Assert.Equal(PaymentStatus.Paid, row.Status); // yarım kalmadı, Pending değil
        Assert.Equal(1, await WithDbAsync(db => db.Invoices.CountAsync(i => i.PaymentId == row.Id, Ct)));
    }

    [Fact]
    public async Task ClientCancellationDuringRefund_RefundStillCompletes()
    {
        var paid = await PayOkAsync();
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
        using var cts = new CancellationTokenSource();
        try
        {
            Factory.Provider.RefundDelay = TimeSpan.FromMilliseconds(500);
            var task = service.RefundAsync(paid.Id, cts.Token);
            await Task.Delay(200, Ct);
            await cts.CancelAsync();
            Assert.Equal(OperationStatus.Ok, (await task).Status);
        }
        finally
        {
            Factory.Provider.Reset();
        }

        Assert.Equal(PaymentStatus.Refunded, (await LoadAsync(paid.Id)).Status);
    }

    // ---------- Bayat Pending ----------

    [Fact]
    public async Task StalePending_SameKeyRetry_Returns409Immediately_WithoutWaitingOrCallingProvider()
    {
        var dto = NewPayment();
        var key = Unique("stale");
        await WithDbAsync(async db =>
        {
            db.Payments.Add(new Api.Entities.Payment
            {
                Id = Guid.CreateVersion7(),
                AppointmentId = dto.AppointmentId,
                PatientId = dto.PatientId,
                DoctorId = dto.DoctorId,
                Amount = dto.Amount,
                Currency = dto.Currency,
                Status = PaymentStatus.Pending,
                IdempotencyKey = key,
                RequestHash = RequestFingerprint.Compute(dto),
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            });
            await db.SaveChangesAsync(Ct);
            return 0;
        });
        var before = Factory.Provider.ChargeCalls;

        var watch = Stopwatch.StartNew();
        var response = await PostAsync(dto, key);
        watch.Stop();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("destekle iletişime", await response.Content.ReadAsStringAsync(Ct));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Bayat Pending beklememeli ({watch.Elapsed}).");
        Assert.Equal(before, Factory.Provider.ChargeCalls);
        Assert.Contains(Factory.Logs.Entries, e => e.StartsWith("[Warning]") && e.Contains("Bayat Pending"));
    }

    // ---------- Replay + farklı kart ----------

    [Fact]
    public async Task Replay_SameKeyDifferentCard_ReturnsFirstResult_Intentionally()
    {
        // Kasıtlı davranış: kart verisini saklamadığımız için gövde karşılaştırması kartı İÇERMEZ.
        // Aynı anahtar + aynı tutar/randevu/hasta ile farklı (hatta reddedilecek) kart gelirse ilk sonuç döner, yeni çekim yapılmaz.
        var dto = NewPayment();
        var key = Unique("card-swap");
        var first = await PostAsync(dto, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var before = Factory.Provider.ChargeCalls;

        var swapped = NewPayment(card: Declined, appointmentId: dto.AppointmentId, patientId: dto.PatientId, doctorId: dto.DoctorId);
        var second = await PostAsync(swapped, key);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await ReadAsync<PaymentResultDto>(first)).Id, (await ReadAsync<PaymentResultDto>(second)).Id);
        Assert.Equal(before, Factory.Provider.ChargeCalls);
    }

    // ---------- Büyük tutarlarda KDV ----------

    [Theory]
    [InlineData("1000000.00")]
    [InlineData("999999.99")]
    public async Task LargeAmounts_InvoiceTotalEqualsAmount(string text)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var paid = await PayOkAsync(NewPayment(amount: amount));
        var invoice = await ReadAsync<GetByIdInvoiceDto>(await Client.GetAsync($"/api/invoices/{paid.InvoiceId}", Ct));

        Assert.Equal(amount, invoice.Total);
        Assert.Equal(amount, invoice.Subtotal + invoice.TaxAmount);
        Assert.Equal(invoice.Subtotal, Assert.Single(invoice.Lines).LineTotal);
    }
}
