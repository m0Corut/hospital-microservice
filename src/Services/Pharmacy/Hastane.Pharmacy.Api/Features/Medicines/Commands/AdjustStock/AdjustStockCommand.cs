using Hastane.Pharmacy.Api.Common;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.AdjustStock;

// Delta: +giriş (mal kabul), -çıkış (fire, sayım düzeltmesi). Eksiye düşecekse 409.
public sealed record AdjustStockCommand(Guid MedicineId, int Delta, string Reason) : IRequest<OperationResult<StockMovementDto>>;
