using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Queries.PreviewDispense;

public sealed class PreviewDispenseValidator : IValidator<PreviewDispenseQuery>
{
    public IEnumerable<ValidationFailure> Validate(PreviewDispenseQuery q)
    {
        if (q.PrescriptionId == Guid.Empty) yield return new("PrescriptionId", "Reçete kimliği gerekli.");
    }
}
