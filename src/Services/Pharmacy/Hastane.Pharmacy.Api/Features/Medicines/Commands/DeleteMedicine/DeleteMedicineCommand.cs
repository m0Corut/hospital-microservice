using Hastane.Pharmacy.Api.Common;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.DeleteMedicine;

public sealed record DeleteMedicineCommand(Guid Id) : IRequest<OperationResult<Unit>>;
