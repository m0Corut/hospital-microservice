using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.Models.Api.Pharmacy;

namespace Hastane.WebUI.Clients;

// Pharmacy servisi: reçete karşılama (Admin ekranları).
public interface IPharmacyDispensingClient
{
    // Salt okunur önizleme. 404 (reçete yok) ve 503 (reçete servisi kapalı) ekranda mesaj olarak gösterilir, hata sayfasına gitmez.
    Task<ApiResult<PharmacyDispensePreviewApiModel>> PreviewAsync(Guid prescriptionId, CancellationToken ct);

    // 201 başarı; 409 (eşleşmeyen/yetersiz stok/zaten karşılandı) ve 503 çağırana döner.
    Task<ApiResult<PharmacyDispensationApiModel>> DispenseAsync(Guid prescriptionId, CancellationToken ct);

    Task<ApiResult<AdminPagedApiModel<PharmacyDispensationApiModel>>> GetDispensationsAsync(int page, int pageSize, CancellationToken ct);
}

public sealed class PharmacyDispensingClient(IGatewayClient gateway) : IPharmacyDispensingClient
{
    public Task<ApiResult<PharmacyDispensePreviewApiModel>> PreviewAsync(Guid prescriptionId, CancellationToken ct)
        => gateway.GetAsync<PharmacyDispensePreviewApiModel>(
            $"/gateway/pharmacy/dispense/preview/{prescriptionId}", ApiFailures.NotFound | ApiFailures.Unavailable, ct);

    public Task<ApiResult<PharmacyDispensationApiModel>> DispenseAsync(Guid prescriptionId, CancellationToken ct)
        => gateway.PostAsync<PharmacyDispensationApiModel>(
            "/gateway/pharmacy/dispense", new { prescriptionId }, ApiFailures.NotFound | ApiFailures.Unavailable, ct);

    public Task<ApiResult<AdminPagedApiModel<PharmacyDispensationApiModel>>> GetDispensationsAsync(int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<AdminPagedApiModel<PharmacyDispensationApiModel>>(
            AdminQuery.Build("/gateway/pharmacy/dispensations",
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
            ct: ct);
}
