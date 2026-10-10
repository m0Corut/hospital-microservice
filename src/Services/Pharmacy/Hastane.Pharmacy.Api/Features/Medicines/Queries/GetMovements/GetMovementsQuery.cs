using Hastane.Pharmacy.Api.Common;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMovements;

public sealed record GetMovementsQuery(Guid MedicineId, int Page = 1, int PageSize = 20)
    : IRequest<OperationResult<PagedResult<StockMovementDto>>>;
