using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.ViewModels.Shared;

namespace Hastane.WebUI.Clients;

// Panel grafikleri için ham veri. Her kaynak bağımsızdır: biri alınamazsa (servis kapalı, yetki) null döner, diğerleri çizilir.
public sealed record AdminDashboardRaw(
    List<AdminAppointmentApiModel>? Appointments,
    List<AdminPaymentApiModel>? Payments,
    long? PaymentTotal,
    List<AdminDoctorListItemApiModel>? Doctors,
    List<AdminBranchApiModel>? Branches,
    DateTime NowUtc);

public interface IAdminDashboardClient
{
    Task<AdminDashboardRaw> LoadAsync(CancellationToken ct);
}

public sealed class AdminDashboardClient(IGatewayClient gateway) : IAdminDashboardClient
{
    public const int WindowDays = 30;
    public const int PaymentPageSize = 50;   // Payment API: pageSize en çok 50
    public const int MaxPayments = 500;      // 10 sayfa; aşılırsa ekran "son 500 kayıt" der

    public async Task<AdminDashboardRaw> LoadAsync(CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var appointments = LoadAppointmentsAsync(nowUtc, ct);
        var payments = LoadPaymentsAsync(ct);
        var doctors = gateway.GetAsync<List<AdminDoctorListItemApiModel>>("/gateway/doctors", ApiFailures.All, ct);
        var branches = gateway.GetAsync<List<AdminBranchApiModel>>("/gateway/branches", ApiFailures.All, ct);
        await Task.WhenAll(appointments, payments, doctors, branches);

        var (pay, total) = await payments;
        return new AdminDashboardRaw((await appointments), pay, total, (await doctors).Value, (await branches).Value, nowUtc);
    }

    // Randevular: İstanbul gününe göre son 30 günün başından itibaren (üst sınır yok: ileri tarihli randevular durum dağılımına girer).
    private async Task<List<AdminAppointmentApiModel>?> LoadAppointmentsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var firstDay = AppointmentTime.ToClinicLocal(nowUtc).Date.AddDays(-(WindowDays - 1));
        var fromUtc = AppointmentTime.ClinicLocalToUtc(firstDay);
        var path = AdminQuery.Build("/gateway/appointments", ("from", fromUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        return (await gateway.GetAsync<List<AdminAppointmentApiModel>>(path, ApiFailures.All, ct)).Value;
    }

    // Ödemeler: en yeniden eskiye sayfa sayfa, en çok MaxPayments kayıt.
    private async Task<(List<AdminPaymentApiModel>? Items, long? Total)> LoadPaymentsAsync(CancellationToken ct)
    {
        var all = new List<AdminPaymentApiModel>();
        long? total = null;
        for (var page = 1; all.Count < MaxPayments; page++)
        {
            var path = $"/gateway/payments?page={page}&pageSize={PaymentPageSize}";
            var result = await gateway.GetAsync<AdminPagedApiModel<AdminPaymentApiModel>>(path, ApiFailures.All, ct);
            if (!result.TryGetValue(out var paged)) return page == 1 ? (null, null) : (all, total);

            total = paged.TotalCount;
            all.AddRange(paged.Items);
            if (paged.Items.Count < PaymentPageSize || all.Count >= paged.TotalCount) break;
        }

        return (all, total);
    }
}
