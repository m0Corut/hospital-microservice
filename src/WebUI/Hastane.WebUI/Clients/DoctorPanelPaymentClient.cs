using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Patient;

namespace Hastane.WebUI.Clients;

// Doktor paneli ödeme/fatura okuması. Aynı uçlar (GET invoices, GET payments); doktor için kapsam serviste
// token'daki doctor_id'den zorlanır, istemci DoctorId göndermez. Modeller hasta panelinin API kopyalarıdır.
public interface IDoctorPanelPaymentClient
{
    Task<ApiResult<PagedPatientApiModel<InvoiceApiModel>>> ListInvoicesAsync(int page, int pageSize, CancellationToken ct);

    Task<ApiResult<InvoiceDetailApiModel>> GetInvoiceAsync(Guid id, CancellationToken ct);

    // Özet kutuları ve "iade" rozeti için: durum süzgeci (Paid / Refunded). Hata olursa sayfa açılsın diye ApiFailures.All.
    Task<ApiResult<PagedPatientApiModel<PaymentApiModel>>> ListPaymentsAsync(string status, int page, int pageSize, CancellationToken ct);

    Task<ApiResult<PaymentApiModel>> GetPaymentAsync(Guid id, CancellationToken ct);
}

public sealed class DoctorPanelPaymentClient(IGatewayClient gateway) : IDoctorPanelPaymentClient
{
    public Task<ApiResult<PagedPatientApiModel<InvoiceApiModel>>> ListInvoicesAsync(int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<PagedPatientApiModel<InvoiceApiModel>>($"/gateway/invoices?page={page}&pageSize={pageSize}", ct: ct);

    public Task<ApiResult<InvoiceDetailApiModel>> GetInvoiceAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<InvoiceDetailApiModel>($"/gateway/invoices/{id}", ct: ct);

    public Task<ApiResult<PagedPatientApiModel<PaymentApiModel>>> ListPaymentsAsync(string status, int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<PagedPatientApiModel<PaymentApiModel>>(
            $"/gateway/payments?status={Uri.EscapeDataString(status)}&page={page}&pageSize={pageSize}", ApiFailures.All, ct);

    public Task<ApiResult<PaymentApiModel>> GetPaymentAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<PaymentApiModel>($"/gateway/payments/{id}", ApiFailures.All, ct);
}
