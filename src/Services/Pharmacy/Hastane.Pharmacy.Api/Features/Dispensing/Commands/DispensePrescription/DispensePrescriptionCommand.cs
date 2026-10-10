using Hastane.Pharmacy.Api.Common;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Commands.DispensePrescription;

public sealed record DispensePrescriptionCommand(Guid PrescriptionId) : IRequest<OperationResult<DispensationDto>>;
