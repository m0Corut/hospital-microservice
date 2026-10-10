using Hastane.Pharmacy.Api.Common;
using MediatR;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Queries.PreviewDispense;

// "Karşıla'ya basarsam ne olur?" sorusu: aynı planı çalıştırır ama hiçbir şeyi değiştirmez.
public sealed record PreviewDispenseQuery(Guid PrescriptionId) : IRequest<OperationResult<DispensePreviewDto>>;
