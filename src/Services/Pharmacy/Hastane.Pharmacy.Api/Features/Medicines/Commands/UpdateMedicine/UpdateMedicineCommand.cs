using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Entities;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.UpdateMedicine;

// Stok burada YOK: stok yalnız /stock ucuyla (hareket kaydıyla) değişir. Böylece "formu açık unutan yönetici eski stok sayısını
// geri yazar" kazası olmaz.
// RowVersion: formu açtığınızdaki sürüm. Gönderilirse ve satır o arada değiştiyse 409 döner (kayıp güncelleme koruması).
public sealed record UpdateMedicineCommand(
    Guid Id,
    string Name,
    string ActiveIngredient,
    MedicineForm Form,
    string Strength,
    string Barcode,
    decimal Price,
    int MinStock,
    bool RequiresPrescription,
    byte[]? RowVersion = null) : IRequest<OperationResult<MedicineDto>>;
