using System.Net;
using Hastane.Payment.Api.Data;
using Hastane.Payment.Api.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Payment.Tests;

// PCI-DSS: tam kart numarası ve CVV saklanamaz. Bu testler varsayımı DEĞİL, veritabanını ve logları tarayarak kanıtlar.
[Collection(PaymentCollection.Name)]
public class CardDataLeakTests : PaymentTestBase
{
    // Luhn geçen, ayırt edici değerler (başka testlerin verisiyle karışmaz).
    private const string SecretCard = "5555555555554444";
    private const string SecretDeclinedCard = "4000000000000002";
    private const string SecretCvv = "7391";
    private const string SecretHolder = "Zeynep Gizlikart";
    private static readonly string[] Secrets = [SecretCard, SecretDeclinedCard, "5555555555554443", SecretCvv, SecretHolder];

    public CardDataLeakTests(PaymentApiFactory factory) : base(factory)
    {
    }

    private static Api.Dtos.CreatePaymentDto Secret(string card)
    {
        var dto = NewPayment(card: card);
        dto.Card.Cvv = SecretCvv;
        dto.Card.HolderName = SecretHolder;
        return dto;
    }

    // Ham SQL: Payments, Invoices, InvoiceLines tablolarındaki TÜM metin sütunlarında aranan değerleri tarar.
    private Task<(List<string> Hits, List<(string Table, string Column)> Columns)> ScanAsync(params string[] needles)
        => WithDbAsync(async db =>
        {
            var connection = (SqlConnection)db.Database.GetDbConnection();
            await connection.OpenAsync(Ct);
            try
            {
                var columns = new List<(string Table, string Column)>();
                await using (var cmd = new SqlCommand(
                    "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
                    "WHERE TABLE_NAME IN ('Payments','Invoices','InvoiceLines') " +
                    "AND DATA_TYPE IN ('char','varchar','nchar','nvarchar','text','ntext')", connection))
                await using (var reader = await cmd.ExecuteReaderAsync(Ct))
                {
                    while (await reader.ReadAsync(Ct)) columns.Add((reader.GetString(0), reader.GetString(1)));
                }

                var found = new List<string>();
                foreach (var (table, column) in columns)
                {
                    foreach (var needle in needles)
                    {
                        // LIKE ile adayları getir, sonra ContainsSecret ile gerçek isabeti ayır (GUID içindeki tesadüfi "7391" yanlış alarmı).
                        await using var cmd = new SqlCommand($"SELECT [{column}] FROM [{table}] WHERE [{column}] LIKE @p", connection);
                        cmd.Parameters.AddWithValue("@p", "%" + needle + "%");
                        await using var reader = await cmd.ExecuteReaderAsync(Ct);
                        while (await reader.ReadAsync(Ct))
                        {
                            if (ContainsSecret(reader.GetString(0), needle))
                            {
                                found.Add($"{table}.{column} içinde {needle.Length} karakterlik değer bulundu");
                                break;
                            }
                        }
                    }
                }
                return (found, columns);
            }
            finally
            {
                await connection.CloseAsync();
            }
        });

    [Fact]
    public async Task ScanFunction_PositiveControl_FindsAPlantedValue()
    {
        // Taramanın kırık olmadığını kanıtla: bilerek bir metin sütununa değer yaz, tarama BULMALI.
        const string planted = "LEAKCONTROL-9f3a1c77";
        await WithDbAsync(async db =>
        {
            db.Payments.Add(new Api.Entities.Payment
            {
                Id = Guid.CreateVersion7(),
                AppointmentId = Guid.CreateVersion7(),
                PatientId = "control",
                DoctorId = "control",
                Amount = 1m,
                Currency = "TRY",
                Status = PaymentStatus.Failed,
                FailureReason = planted,
                IdempotencyKey = Unique("control"),
                RequestHash = new string('c', 64),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(Ct);
            return 0;
        });

        var (hits, columns) = await ScanAsync(planted);

        Assert.Contains("Payments.FailureReason içinde " + planted.Length + " karakterlik değer bulundu", hits);
        Assert.Contains(("Payments", "CardLast4"), columns);
        Assert.Contains(("Invoices", "InvoiceNumber"), columns);
        Assert.Contains(("InvoiceLines", "Description"), columns);
    }

    [Fact]
    public async Task AfterPayments_NoTextColumnContainsCardNumberOrCvv_AndResponsesStaySafe()
    {
        var bodies = new List<string>();

        foreach (var card in new[] { SecretCard, SecretDeclinedCard })
        {
            var response = await PostAsync(Secret(card));
            Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.PaymentRequired);
            bodies.Add(await response.Content.ReadAsStringAsync(Ct));
        }

        // Hatalı istek (geçersiz Luhn) cevabı da kart verisi yansıtmamalı.
        var badResponse = await PostAsync(Secret("5555555555554443"));
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);
        bodies.Add(await badResponse.Content.ReadAsStringAsync(Ct));

        // Liste cevabı da.
        bodies.Add(await (await Client.GetAsync("/api/payments?pageSize=50", Ct)).Content.ReadAsStringAsync(Ct));

        foreach (var body in bodies)
        {
            AssertNoSecret(body, Secrets);
        }

        var (hits, _) = await ScanAsync(Secrets);
        Assert.Empty(hits);
    }

    [Fact]
    public async Task LogsAndExceptionTexts_NeverContainCardData_AcrossSuccessDeclineErrorAndRefundFlows()
    {
        var provider = Factory.Provider;
        var logsBefore = Factory.Logs.Entries.Count;
        try
        {
            // Başarı
            var ok = await PostAsync(Secret(SecretCard));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
            var paid = await ReadAsync<Api.Dtos.PaymentResultDto>(ok);

            // Red
            Assert.Equal(HttpStatusCode.PaymentRequired, (await PostAsync(Secret(SecretDeclinedCard))).StatusCode);

            // Doğrulama hatası
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(Secret("5555555555554443"))).StatusCode);

            // Sağlayıcı hatası (istisna loglanır)
            provider.ChargeException = new InvalidOperationException("sağlayıcı çöktü (test)");
            Assert.Equal(HttpStatusCode.BadGateway, (await PostAsync(Secret(SecretCard))).StatusCode);
            provider.Reset();

            // İade: başarı, ret ve istisna
            provider.RefundMode = RefundMode.Throw;
            Assert.Equal(HttpStatusCode.BadGateway, (await Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct)).StatusCode);
            provider.RefundMode = RefundMode.Reject;
            Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct)).StatusCode);
            provider.Reset();
            Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync($"/api/payments/{paid.Id}/refund", null, Ct)).StatusCode);
        }
        finally
        {
            provider.Reset();
        }

        var entries = Factory.Logs.Entries.Skip(logsBefore).ToList();

        // Pozitif kontrol: yakalayıcı gerçekten çalışıyor ve hata yollarını gördü.
        Assert.Contains(entries, e => e.Contains("Ödeme sağlayıcısı hata verdi"));
        Assert.Contains(entries, e => e.Contains("Sağlayıcı iadesi hata verdi"));
        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            AssertNoSecret(entry, Secrets);
        }
    }
}
