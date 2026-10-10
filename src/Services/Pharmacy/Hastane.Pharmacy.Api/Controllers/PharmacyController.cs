using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Features.Dispensing.Commands.DispensePrescription;
using Hastane.Pharmacy.Api.Features.Dispensing.Queries.GetDispensationByPrescription;
using Hastane.Pharmacy.Api.Features.Dispensing.Queries.GetDispensations;
using Hastane.Pharmacy.Api.Features.Dispensing.Queries.PreviewDispense;
using Hastane.Shared.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Pharmacy.Api.Controllers;

// Reçete karşılama. Eczacı rolü yok: bütün uçlar Admin.
[Route("api/pharmacy")]
public class PharmacyController(ISender sender) : PharmacyApiController(sender)
{
    // Controller → ISender → Handler: bu eylem tüm yolun en kısa örneği. Gövde {prescriptionId} doğrudan komuta bağlanır.
    [HttpPost("dispense")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> Dispense(DispensePrescriptionCommand command, CancellationToken ct)
    {
        var result = await Sender.Send(command, ct);
        return result.Status == OperationStatus.Created
            ? CreatedAtAction(nameof(GetDispensationByPrescription), new { prescriptionId = result.Value!.PrescriptionId }, result.Value)
            : Failure(result);
    }

    // Salt okunur önizleme: "Karşıla'ya basarsam ne olur?" (stoğa dokunmaz).
    [HttpGet("dispense/preview/{prescriptionId:guid}")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> PreviewDispense(Guid prescriptionId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new PreviewDispenseQuery(prescriptionId), ct));

    [HttpGet("dispensations")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> GetDispensations([FromQuery] GetDispensationsQuery query, CancellationToken ct)
        => ToActionResult(await Sender.Send(query, ct));

    [HttpGet("dispensations/by-prescription/{prescriptionId:guid}")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> GetDispensationByPrescription(Guid prescriptionId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetDispensationByPrescriptionQuery(prescriptionId), ct));
}
