using Hastane.Payment.Api.Dtos;
using Hastane.Payment.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Payment.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _service;

    public InvoicesController(IInvoiceService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ResultInvoiceDto>>> GetAll([FromQuery] InvoiceListQuery query, CancellationToken ct)
    {
        var result = await _service.GetAllAsync(query, ct);
        return result.Status == OperationStatus.Validation
            ? BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = result.Error })
            : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetByIdInvoiceDto>> GetById(Guid id, CancellationToken ct)
    {
        var invoice = await _service.GetByIdAsync(id, ct);
        return invoice is null ? NotFound() : Ok(invoice);
    }
}
