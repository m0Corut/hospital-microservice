using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.Models.Api.Pharmacy;

namespace Hastane.WebUI.Clients;

// Pharmacy servisi: ilaç kataloğu ve stok (Admin ekranları). API yolları yalnız burada.
public interface IPharmacyMedicineClient
{
    Task<ApiResult<AdminPagedApiModel<PharmacyMedicineApiModel>>> GetPageAsync(string? search, string? form, int page, int pageSize, CancellationToken ct);

    Task<ApiResult<PharmacyMedicineApiModel>> GetAsync(Guid id, CancellationToken ct);

    Task<ApiResult<PharmacyMedicineApiModel>> CreateAsync(PharmacyMedicineWriteBody body, CancellationToken ct);

    Task<ApiResult<PharmacyMedicineApiModel>> UpdateAsync(Guid id, PharmacyMedicineWriteBody body, CancellationToken ct);

    Task<ApiResult> DeleteAsync(Guid id, CancellationToken ct);

    // Stok eksiye düşecekse API 409 döner (Conflict), ekran mesajı gösterir.
    Task<ApiResult<PharmacyMovementApiModel>> AdjustStockAsync(Guid id, int delta, string reason, CancellationToken ct);

    Task<ApiResult<AdminPagedApiModel<PharmacyMovementApiModel>>> GetMovementsAsync(Guid id, int page, int pageSize, CancellationToken ct);
}

public sealed class PharmacyMedicineClient(IGatewayClient gateway) : IPharmacyMedicineClient
{
    public Task<ApiResult<AdminPagedApiModel<PharmacyMedicineApiModel>>> GetPageAsync(string? search, string? form, int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<AdminPagedApiModel<PharmacyMedicineApiModel>>(
            AdminQuery.Build("/gateway/medicines",
                ("search", search), ("form", form),
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
            ct: ct);

    public Task<ApiResult<PharmacyMedicineApiModel>> GetAsync(Guid id, CancellationToken ct)
        => gateway.GetAsync<PharmacyMedicineApiModel>($"/gateway/medicines/{id}", ct: ct);

    public Task<ApiResult<PharmacyMedicineApiModel>> CreateAsync(PharmacyMedicineWriteBody body, CancellationToken ct)
        => gateway.PostAsync<PharmacyMedicineApiModel>("/gateway/medicines", body, ct: ct);

    public Task<ApiResult<PharmacyMedicineApiModel>> UpdateAsync(Guid id, PharmacyMedicineWriteBody body, CancellationToken ct)
        => gateway.PutAsync<PharmacyMedicineApiModel>($"/gateway/medicines/{id}", body, ct: ct);

    public Task<ApiResult> DeleteAsync(Guid id, CancellationToken ct)
        => gateway.DeleteAsync($"/gateway/medicines/{id}", ct: ct);

    public Task<ApiResult<PharmacyMovementApiModel>> AdjustStockAsync(Guid id, int delta, string reason, CancellationToken ct)
        => gateway.PostAsync<PharmacyMovementApiModel>($"/gateway/medicines/{id}/stock", new PharmacyStockAdjustBody { Delta = delta, Reason = reason }, ct: ct);

    public Task<ApiResult<AdminPagedApiModel<PharmacyMovementApiModel>>> GetMovementsAsync(Guid id, int page, int pageSize, CancellationToken ct)
        => gateway.GetAsync<AdminPagedApiModel<PharmacyMovementApiModel>>(
            AdminQuery.Build($"/gateway/medicines/{id}/movements",
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture))),
            ct: ct);
}
