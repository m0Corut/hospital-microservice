using Hastane.Payment.Api.Dtos;
using Hastane.Payment.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Payment.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public class InvoicesController : ControllerBase
{
    // Her eylem kendi yetkisini AÇIKÇA taşır; "kimin faturası" sorusunu rol değil servis (sahiplik) cevaplar.
    private const string AnyPaymentRole = $"{HastaneRoles.Admin},{HastaneRoles.Doctor},{HastaneRoles.Patient}";

    private readonly IInvoiceService _service;

    public InvoicesController(IInvoiceService service)
    {
        _service = service;
    }

    [HttpGet]
    [Authorize(Roles = AnyPaymentRole)]
    public async Task<ActionResult<PagedResult<ResultInvoiceDto>>> GetAll([FromQuery] InvoiceListQuery query, CancellationToken ct)
    {
        var result = await _service.GetAllAsync(query, ct);
        return result.Status == OperationStatus.Validation
            ? BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = result.Error })
            : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = AnyPaymentRole)]
    public async Task<ActionResult<GetByIdInvoiceDto>> GetById(Guid id, CancellationToken ct)
    {
        var invoice = await _service.GetByIdAsync(id, ct);
        return invoice is null ? NotFound() : Ok(invoice);
    }
}
