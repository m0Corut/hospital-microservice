using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.DeleteMedicine;

public sealed class DeleteMedicineValidator : IValidator<DeleteMedicineCommand>
{
    public IEnumerable<ValidationFailure> Validate(DeleteMedicineCommand c)
    {
        if (c.Id == Guid.Empty) yield return new("Id", "İlaç kimliği gerekli.");
    }
}
