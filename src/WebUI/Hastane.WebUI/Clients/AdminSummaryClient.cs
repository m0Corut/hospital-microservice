using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.Clients;

// Yönetim paneli özet kutuları: yalnız servislerin sayfalı uçlarının toplam sayısı (totalCount). pageSize=1 ile en az veri çekilir.
// Sayı alınamazsa (servis kapalı, yetki) null döner ve kutu hiç gösterilmez: uydurma sıfır basılmaz.
public sealed record AdminSummary(long? Doctors, long? Payments, long? Invoices);

public interface IAdminSummaryClient
{
    Task<AdminSummary> LoadAsync(CancellationToken ct);
}

public sealed class AdminSummaryClient(IGatewayClient gateway) : IAdminSummaryClient
{
    public async Task<AdminSummary> LoadAsync(CancellationToken ct)
    {
        var doctors = CountAsync<AdminDoctorListItemApiModel>("/gateway/doctors/search?page=1&pageSize=1", ct);
        var payments = CountAsync<AdminPaymentApiModel>("/gateway/payments?page=1&pageSize=1", ct);
        var invoices = CountAsync<AdminInvoiceApiModel>("/gateway/invoices?page=1&pageSize=1", ct);
        await Task.WhenAll(doctors, payments, invoices);
        return new AdminSummary(await doctors, await payments, await invoices);
    }

    private async Task<long?> CountAsync<T>(string path, CancellationToken ct)
    {
        var result = await gateway.GetAsync<AdminPagedApiModel<T>>(path, ApiFailures.All, ct);
        return result.TryGetValue(out var page) ? page.TotalCount : null;
    }
}
