using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Queries.GetDispensations;

public sealed class GetDispensationsValidator : IValidator<GetDispensationsQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetDispensationsQuery q)
    {
        var failures = new List<ValidationFailure>();
        Rules.Paging(failures, q.Page, q.PageSize);
        return failures;
    }
}
