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

public sealed class PatientPaymentClient(IGatewayClient gateway)
    : IPatientPaymentClient
{
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
    => gateway.PostAsync<PaymentApiModel>("/gateway/payments", request,
            new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey }, handle, ct);

    public Task<ApiResult<PagedPatientApiModel<InvoiceApiModel>>> ListInvoicesAsync(int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<PagedPatientApiModel<InvoiceApiModel>>($"/gateway/invoices?page={page}&pageSize={pageSize}", ct: ct);

    public Task<ApiResult<InvoiceDetailApiModel>> GetInvoiceAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<InvoiceDetailApiModel>($"/gateway/invoices/{id}", ct: ct);
}
