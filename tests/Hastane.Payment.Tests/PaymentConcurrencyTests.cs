using System.Net;
using System.Text.RegularExpressions;
using Hastane.Payment.Api.Dtos;
using Hastane.Payment.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Payment.Tests;

[Collection(PaymentCollection.Name)]
public class PaymentConcurrencyTests : PaymentTestBase
{
    private const int Parallel = 8;

    public PaymentConcurrencyTests(PaymentApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task SameKey_8ParallelPosts_OnePayment_OneProviderCall()
    {
        var dto = NewPayment();
        var key = Unique("race-key");
        var before = Factory.Provider.ChargeCalls;

        var responses = await Task.WhenAll(Enumerable.Range(0, Parallel).Select(_ => PostAsync(dto, key)));

        // Kazanan 201; kalanlar kazananın sonucunu aynen alır (200). Hiçbiri hata değil.
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(Parallel - 1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var ids = new HashSet<Guid>();
        foreach (var r in responses) ids.Add((await ReadAsync<PaymentResultDto>(r)).Id);
        Assert.Single(ids);

        Assert.Equal(before + 1, Factory.Provider.ChargeCalls);
        Assert.Equal(1, await WithDbAsync(db => db.Payments.CountAsync(p => p.IdempotencyKey == key, Ct)));
        Assert.Equal(1, await WithDbAsync(db => db.Invoices.CountAsync(i => i.PatientId == dto.PatientId, Ct)));
    }

    [Fact]
    public async Task SameAppointment_8ParallelPosts_DifferentKeys_OneCreated_RestConflict_OnePaidRow()
    {
        var appointmentId = Guid.CreateVersion7();
        var chargesBefore = Factory.Provider.ChargeCalls;
        var refundsBefore = Factory.Provider.RefundCalls;

        // Aynı randevu = aynı hasta ve doktor (sahte servisler randevuya tek hasta/doktor kaydeder).
        var patientId = Unique("pat");
        var doctorId = Unique("doc");
        var responses = await Task.WhenAll(Enumerable.Range(0, Parallel)
            .Select(_ => PostAsync(NewPayment(appointmentId: appointmentId, patientId: patientId, doctorId: doctorId))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(Parallel - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var paid = await WithDbAsync(db => db.Payments.CountAsync(p => p.AppointmentId == appointmentId && p.Status == PaymentStatus.Paid, Ct));
        Assert.Equal(1, paid);
        Assert.Equal(1, await WithDbAsync(db => db.Invoices.CountAsync(i => i.Payment!.AppointmentId == appointmentId, Ct)));

        // Para korunumu: yarışı kaybedip yine de kartı çeken her istek için sağlayıcıya iade gitti.
        var charges = Factory.Provider.ChargeCalls - chargesBefore;
        var refunds = Factory.Provider.RefundCalls - refundsBefore;
        Assert.True(charges > 1, "Yarış oluşmadı: telafi yolu çalışmadı, test anlamsız.");
        Assert.Equal(charges - 1, refunds);

        // Telafi edilen satırlar Failed (Duplicate) olarak kapandı; Pending kalan yok.
        var failedDuplicates = await WithDbAsync(db => db.Payments.CountAsync(
            p => p.AppointmentId == appointmentId && p.Status == PaymentStatus.Failed && p.FailureKind == PaymentFailureKind.Duplicate, Ct));
        Assert.Equal(charges - 1, failedDuplicates);
        Assert.Equal(0, await WithDbAsync(db => db.Payments.CountAsync(p => p.AppointmentId == appointmentId && p.Status == PaymentStatus.Pending, Ct)));
    }

    [Fact]
    public async Task InvoiceNumbers_8ParallelPayments_AllUniqueAndWellFormed()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, Parallel).Select(_ => PostAsync(NewPayment())));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var numbers = new List<string>();
        foreach (var r in responses) numbers.Add((await ReadAsync<PaymentResultDto>(r)).InvoiceNumber!);

        Assert.Equal(Parallel, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches(new Regex(@"^INV-\d{4}-\d{6}$"), n));
    }

    [Fact]
    public async Task Refund_8ParallelRequests_OneSucceeds_ProviderRefundedOnce()
    {
        var paid = await PayOkAsync();
        var before = Factory.Provider.RefundCalls;

        var responses = await Task.WhenAll(Enumerable.Range(0, Parallel)
            .Select(_ => Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(Parallel - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(before + 1, Factory.Provider.RefundCalls);
    }
}
