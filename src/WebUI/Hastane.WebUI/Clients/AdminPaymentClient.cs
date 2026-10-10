using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.Clients;

// Payment servisi: ödemeler ve faturalar (aynı servis, iki rota).
public interface IAdminPaymentClient
{
    Task<ApiResult<AdminPagedApiModel<AdminPaymentApiModel>>> GetPaymentsAsync(string? status, string? patientId, string? doctorId, int page, int pageSize, CancellationToken ct);

    Task<ApiResult<AdminPaymentApiModel>> GetPaymentAsync(Guid id, CancellationToken ct);

    // İade: idempotency ve iade kuralları backend'dedir; 400/409 çağırana döner (ekran Detail'i gösterir).
    Task<ApiResult<AdminPaymentApiModel>> RefundAsync(Guid id, CancellationToken ct);

    Task<ApiResult<AdminPagedApiModel<AdminInvoiceApiModel>>> GetInvoicesAsync(string? patientId, string? doctorId, int page, int pageSize, CancellationToken ct);

    Task<ApiResult<AdminInvoiceApiModel>> GetInvoiceAsync(Guid id, CancellationToken ct);
}

public sealed class AdminPaymentClient(IGatewayClient gateway) : IAdminPaymentClient
{
    public Task<ApiResult<AdminPagedApiModel<AdminPaymentApiModel>>> GetPaymentsAsync(string? status, string? patientId, string? doctorId, int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<AdminPagedApiModel<AdminPaymentApiModel>>(
            AdminQuery.Build("/gateway/payments",
                ("status", status), ("patientId", patientId), ("doctorId", doctorId),
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
            ct: ct);

    public Task<ApiResult<AdminPaymentApiModel>> GetPaymentAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<AdminPaymentApiModel>($"/gateway/payments/{id}", ct: ct);

    public Task<ApiResult<AdminPaymentApiModel>> RefundAsync(Guid id, CancellationToken ct)
        => gateway.PostAsync<AdminPaymentApiModel>($"/gateway/payments/{id}/refund", null, ct: ct);

    public Task<ApiResult<AdminPagedApiModel<AdminInvoiceApiModel>>> GetInvoicesAsync(string? patientId, string? doctorId, int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<AdminPagedApiModel<AdminInvoiceApiModel>>(
            AdminQuery.Build("/gateway/invoices",
                ("patientId", patientId), ("doctorId", doctorId),
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
            ct: ct);

    public Task<ApiResult<AdminInvoiceApiModel>> GetInvoiceAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<AdminInvoiceApiModel>($"/gateway/invoices/{id}", ct: ct);
}
