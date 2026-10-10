using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Features.Medicines;
using Hastane.Pharmacy.Api.Features.Medicines.Commands.AdjustStock;
using Hastane.Pharmacy.Api.Features.Medicines.Commands.CreateMedicine;
using Hastane.Pharmacy.Api.Features.Medicines.Commands.DeleteMedicine;
using Hastane.Pharmacy.Api.Features.Medicines.Commands.UpdateMedicine;
using Hastane.Pharmacy.Api.Features.Medicines.Queries.GetLowStock;
using Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMedicineById;
using Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMedicines;
using Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMovements;
using Hastane.Shared.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Pharmacy.Api.Controllers;

[Route("api/medicines")]
public class MedicinesController(ISender sender) : PharmacyApiController(sender)
{
    // Katalog herkese (giriş yapmış) açık; yazma ve stok uçları Admin. Her eylem kendi etiketini taşır.

    [HttpGet]
    [Authorize(Policy = HastanePolicies.AuthenticatedUser)]
    public async Task<IActionResult> GetAll([FromQuery] GetMedicinesQuery query, CancellationToken ct)
        => ToActionResult(await Sender.Send(query, ct));

    // "low-stock" sabit yol; {id:guid} kısıtı Guid olmayan metni zaten eşleştirmez, çakışma yok.
    [HttpGet("low-stock")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> GetLowStock([FromQuery] GetLowStockQuery query, CancellationToken ct)
        => ToActionResult(await Sender.Send(query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = HastanePolicies.AuthenticatedUser)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetMedicineByIdQuery(id), ct));

    [HttpPost]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> Create(CreateMedicineCommand command, CancellationToken ct)
    {
        var result = await Sender.Send(command, ct);
        return result.Status == OperationStatus.Created
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : Failure(result);
    }

    // Kimlik yoldan gelir; gövdedeki Id (varsa) yok sayılır.
    [HttpPut("{id:guid}")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> Update(Guid id, UpdateMedicineCommand command, CancellationToken ct)
        => ToActionResult(await Sender.Send(command with { Id = id }, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => ToNoContent(await Sender.Send(new DeleteMedicineCommand(id), ct));

    [HttpPost("{id:guid}/stock")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> AdjustStock(Guid id, AdjustStockCommand command, CancellationToken ct)
        => ToActionResult(await Sender.Send(command with { MedicineId = id }, ct));

    [HttpGet("{id:guid}/movements")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> GetMovements(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => ToActionResult(await Sender.Send(new GetMovementsQuery(id, page, pageSize), ct));
}
