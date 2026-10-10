using Hastane.Pharmacy.Api.Common;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetLowStock;

// Düşük stok = Stock <= MinStock (asgari stoğa eşit ya da altı).
public sealed record GetLowStockQuery(int Page = 1, int PageSize = 50) : IRequest<OperationResult<PagedResult<MedicineDto>>>;
