using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Payment.Api.Data;
using Hastane.Payment.Api.Dtos;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Payment.Tests;

public abstract class PaymentTestBase
{
    public const string VisaOk = "4242424242424242";
    public const string Declined = "4000000000000002";
    public const string NoFunds = "4000000000009995";

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected readonly PaymentApiFactory Factory;
    protected readonly HttpClient Client;

    protected PaymentTestBase(PaymentApiFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    protected static CreatePaymentDto NewPayment(
        decimal amount = 110m, string card = VisaOk, Guid? appointmentId = null,
        string? patientId = null, string? doctorId = null, string currency = "TRY") => new()
        {
            AppointmentId = appointmentId ?? Guid.CreateVersion7(),
            PatientId = patientId ?? Unique("pat"),
            DoctorId = doctorId ?? Unique("doc"),
            Amount = amount,
            Currency = currency,
            Description = "Kardiyoloji muayenesi",
            Card = new CardDto
            {
                Number = card,
                ExpMonth = 12,
                ExpYear = DateTime.UtcNow.Year + 2,
                Cvv = "123",
                HolderName = "Test Hasta",
            },
        };

    protected Task<HttpResponseMessage> PostAsync(CreatePaymentDto dto, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments") { Content = JsonContent.Create(dto, options: Json) };
        request.Headers.Add("Idempotency-Key", key ?? Unique("key"));
        return Client.SendAsync(request, Ct);
    }

    protected Task<HttpResponseMessage> PostWithoutKeyAsync(CreatePaymentDto dto)
        => Client.PostAsJsonAsync("/api/payments", dto, Json, Ct);

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;

    protected async Task<PaymentResultDto> PayOkAsync(CreatePaymentDto? dto = null)
    {
        var response = await PostAsync(dto ?? NewPayment());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<PaymentResultDto>(response);
    }

    protected async Task<PagedResult<PaymentResultDto>> ListPaymentsAsync(string query)
    {
        var response = await Client.GetAsync("/api/payments?" + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<PagedResult<PaymentResultDto>>(response);
    }

    // Doğrudan veritabanına bakmak için kısa ömürlü bir kapsam.
    protected async Task<T> WithDbAsync<T>(Func<PaymentDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<PaymentDbContext>());
    }

    // Kısa gizli değerler (4 haneli CVV) GUID/zaman damgası içinde tesadüfen geçebilir. Onaltılık rakam, '.', '-', ':' ile
    // BİTİŞİK olmayan, bağımsız bir sayı ise gerçek sızıntı sayılır. Uzun değerler (kart no, isim) düz Contains.
    public static bool ContainsSecret(string text, string secret)
        => secret.All(char.IsAsciiDigit) && secret.Length < 12
            ? System.Text.RegularExpressions.Regex.IsMatch(text, "(?<![0-9a-fA-F.:-])" + secret + "(?![0-9a-fA-F.:-])")
            : text.Contains(secret, StringComparison.Ordinal);

    public static void AssertNoSecret(string text, params string[] secrets)
    {
        foreach (var secret in secrets)
            Assert.False(ContainsSecret(text, secret), $"Metin gizli değer içeriyor ({secret.Length} karakterlik).");
    }
}
