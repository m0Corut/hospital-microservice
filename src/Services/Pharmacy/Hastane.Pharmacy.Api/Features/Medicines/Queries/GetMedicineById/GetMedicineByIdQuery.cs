using Hastane.Pharmacy.Api.Common;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMedicineById;

public sealed record GetMedicineByIdQuery(Guid Id) : IRequest<OperationResult<MedicineDto>>;
