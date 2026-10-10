using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Dispensing.Queries.PreviewDispense;

public sealed class PreviewDispenseHandler(PharmacyDbContext db, DispensePlanner planner)
    : IRequestHandler<PreviewDispenseQuery, OperationResult<DispensePreviewDto>>
{
    public async Task<OperationResult<DispensePreviewDto>> Handle(PreviewDispenseQuery query, CancellationToken ct)
    {
        var planResult = await planner.PlanAsync(query.PrescriptionId, ct);
        if (!planResult.IsSuccess) return OperationResult<DispensePreviewDto>.Failure(planResult);

        var plan = planResult.Value!;
        var already = await db.Dispensations.AsNoTracking().AnyAsync(d => d.PrescriptionId == query.PrescriptionId, ct);

        return OperationResult<DispensePreviewDto>.Ok(new DispensePreviewDto(
            plan.Prescription.Id,
            plan.Prescription.PatientId,
            plan.Prescription.DoctorId,
            already,
            CanDispense: plan.CanDispense && !already,
            plan.Lines));
    }
}
