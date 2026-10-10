using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetLowStock;

public sealed class GetLowStockValidator : IValidator<GetLowStockQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetLowStockQuery q)
    {
        var failures = new List<ValidationFailure>();
        Rules.Paging(failures, q.Page, q.PageSize);
        return failures;
    }
}
