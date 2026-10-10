using Hastane.Market.Api.Dtos;
using Hastane.Market.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Market.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController(IProductService service, ICurrentUser user) : ControllerBase
{
    // Listeleme ve tek ürün herkese açık (mağaza vitrini). Anonim uçta bile token varsa kullanıcı tanınır:
    // Admin pasif ürünleri de görebilir.
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<PagedResult<ResultProductDto>>> GetAll([FromQuery] ProductListQuery query, CancellationToken ct)
    {
        var result = await service.GetAllAsync(query, user.IsAdmin, ct);
        return result.Status == OperationStatus.Validation
            ? BadRequest(new ProblemDetails { Status = 400, Title = "Geçersiz istek", Detail = result.Error })
            : Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<ResultProductDto>> GetById(Guid id, CancellationToken ct)
    {
        var product = await service.GetByIdAsync(id, user.IsAdmin, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<ActionResult<ResultProductDto>> Create(CreateProductDto dto, CancellationToken ct)
    {
        var result = await service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<ActionResult<ResultProductDto>> Update(Guid id, UpdateProductDto dto, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, dto, ct);
        return result.Status switch
        {
            OperationStatus.Ok => Ok(result.Value),
            OperationStatus.NotFound => NotFound(),
            _ => Conflict(new ProblemDetails { Status = 409, Title = "Çakışma", Detail = result.Error }),
        };
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = HastanePolicies.AdminOnly)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
        return result.Status switch
        {
            OperationStatus.Ok => NoContent(),
            OperationStatus.NotFound => NotFound(),
            _ => Conflict(new ProblemDetails { Status = 409, Title = "Çakışma", Detail = result.Error }),
        };
    }
}
