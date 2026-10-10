using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Patient;

namespace Hastane.WebUI.Clients;

public interface IPatientPaymentClient
{
    Task<ApiResult<PagedPatientApiModel<PaymentApiModel>>> ListPaymentsAsync(int page, int pageSize, Guid? appointmentId, string? status, ApiFailures handle, CancellationToken ct);

    Task<ApiResult<PaymentApiModel>> GetPaymentAsync(Guid id, CancellationToken ct);

    // Idempotency-Key başlığı zorunlu. Aynı anahtar = aynı ödeme niyeti: tekrar gönderilirse sunucu ilk sonucu döndürür, kartı yeniden çekmez.
    Task<ApiResult<PaymentApiModel>> CreatePaymentAsync(string idempotencyKey, CreatePaymentRequest request, ApiFailures handle, CancellationToken ct);

    Task<ApiResult<PagedPatientApiModel<InvoiceApiModel>>> ListInvoicesAsync(int page, int pageSize, CancellationToken ct);

    Task<ApiResult<InvoiceDetailApiModel>> GetInvoiceAsync(Guid id, CancellationToken ct);
}

// IGatewayClient başlık eklemeyi desteklemiyor ve Infrastructure'a dokunmak yasak. Bu yüzden Idempotency-Key için,
// IGatewayClient'ın kullandığı aynı yapılandırılmış HttpClient'in (adres + Bearer handler) yeni bir örneği alınır,
// başlık o örneğin varsayılanına yazılır ve hata eşleme mantığı için GatewayClient aynen kullanılır.
// Örnek her çağrıda yenidir (CreateClient), başlık başka isteğe sızmaz. Kalıcı çözüm: ortak istemci katmanında ele alınabilir.
public sealed class PatientPaymentClient(IGatewayClient gateway, IHttpClientFactory httpFactory, ILogger<GatewayClient> gatewayLogger)
    : IPatientPaymentClient
{
    // AddHttpClient<IGatewayClient, GatewayClient> kaydının adlandırılmış istemci adı.
    private const string GatewayHttpClientName = nameof(IGatewayClient);

    public Task<ApiResult<PagedPatientApiModel<PaymentApiModel>>> ListPaymentsAsync(
        int page, int pageSize, Guid? appointmentId, string? status, ApiFailures handle, CancellationToken ct)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (appointmentId is { } appointment) query.Add($"appointmentId={appointment}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add("status=" + Uri.EscapeDataString(status));
        return gateway.GetAsync<PagedPatientApiModel<PaymentApiModel>>("/gateway/payments?" + string.Join('&', query), handle, ct);
    }

    public Task<ApiResult<PaymentApiModel>> GetPaymentAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<PaymentApiModel>($"/gateway/payments/{id}", ct: ct);

    public Task<ApiResult<PaymentApiModel>> CreatePaymentAsync(string idempotencyKey, CreatePaymentRequest request, ApiFailures handle, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(GatewayHttpClientName);
        http.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        return new GatewayClient(http, gatewayLogger).PostAsync<PaymentApiModel>("/gateway/payments", request, handle, ct);
    }

    public Task<ApiResult<PagedPatientApiModel<InvoiceApiModel>>> ListInvoicesAsync(int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<PagedPatientApiModel<InvoiceApiModel>>($"/gateway/invoices?page={page}&pageSize={pageSize}", ct: ct);

    public Task<ApiResult<InvoiceDetailApiModel>> GetInvoiceAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<InvoiceDetailApiModel>($"/gateway/invoices/{id}", ct: ct);
}
