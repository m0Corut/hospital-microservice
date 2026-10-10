using Hastane.Pharmacy.Api.Common;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Queries.GetDispensations;

public sealed record GetDispensationsQuery(int Page = 1, int PageSize = 20) : IRequest<OperationResult<PagedResult<DispensationDto>>>;
