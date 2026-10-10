using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Entities;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.CreateMedicine;

// KOMUT = "sistemde bir şeyi değiştir" niyeti. Veri taşıyan değişmez bir kayıt; ne yapılacağı Handler'da.
public sealed record CreateMedicineCommand(
    string Name,
    string ActiveIngredient,
    MedicineForm Form,
    string Strength,
    string Barcode,
    decimal Price,
    int Stock,
    int MinStock,
    bool RequiresPrescription) : IRequest<OperationResult<MedicineDto>>;
