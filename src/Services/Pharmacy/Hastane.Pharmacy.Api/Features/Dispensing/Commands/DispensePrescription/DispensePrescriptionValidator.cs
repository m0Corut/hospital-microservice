using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Commands.DispensePrescription;

public sealed class DispensePrescriptionValidator : IValidator<DispensePrescriptionCommand>
{
    public IEnumerable<ValidationFailure> Validate(DispensePrescriptionCommand c)
    {
        if (c.PrescriptionId == Guid.Empty) yield return new("PrescriptionId", "Reçete kimliği gerekli.");
    }
}
