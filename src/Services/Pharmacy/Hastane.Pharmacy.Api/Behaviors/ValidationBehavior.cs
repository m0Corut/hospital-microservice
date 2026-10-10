using Hastane.Pharmacy.Api.Validation;
using MediatR;

namespace Hastane.Pharmacy.Api.Behaviors;

// Pipeline davranışı = her istek handler'a varmadan ÖNCE bu halkadan geçer (middleware'in MediatR sürümü).
// Geçersiz istek burada kesilir: handler hiç çalışmaz, veritabanına dokunulmaz.
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var failures = validators.SelectMany(v => v.Validate(request)).ToList();
        if (failures.Count > 0) throw new RequestValidationException(failures);

        return await next(cancellationToken);
    }
}
