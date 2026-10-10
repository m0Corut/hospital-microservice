using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMovements;

public sealed class GetMovementsValidator : IValidator<GetMovementsQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetMovementsQuery q)
    {
        var failures = new List<ValidationFailure>();
        if (q.MedicineId == Guid.Empty) failures.Add(new("MedicineId", "İlaç kimliği gerekli."));
        Rules.Paging(failures, q.Page, q.PageSize);
        return failures;
    }
}
