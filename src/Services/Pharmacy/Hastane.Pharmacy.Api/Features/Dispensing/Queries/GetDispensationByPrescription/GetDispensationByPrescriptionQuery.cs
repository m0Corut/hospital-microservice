using Hastane.Pharmacy.Api.Common;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Queries.GetDispensationByPrescription;

public sealed record GetDispensationByPrescriptionQuery(Guid PrescriptionId) : IRequest<OperationResult<DispensationDto>>;
