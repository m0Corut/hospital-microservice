using Hastane.Payment.Api.Data;
using Hastane.Payment.Api.Dtos;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Payment.Api.Services;

public class InvoiceService : IInvoiceService
{
    private readonly PaymentDbContext _db;
    private readonly IMapper _mapper;

    public InvoiceService(PaymentDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<OperationResult<PagedResult<ResultInvoiceDto>>> GetAllAsync(InvoiceListQuery query, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 50)
            return OperationResult<PagedResult<ResultInvoiceDto>>.Validation("page >= 1 ve pageSize 1-50 arasında olmalı.");

        var invoices = _db.Invoices.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.PatientId)) invoices = invoices.Where(i => i.PatientId == query.PatientId);
        if (!string.IsNullOrWhiteSpace(query.DoctorId)) invoices = invoices.Where(i => i.DoctorId == query.DoctorId);

        var total = await invoices.CountAsync(ct);
        var items = await invoices
            .OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(ct);

        return OperationResult<PagedResult<ResultInvoiceDto>>.Ok(new PagedResult<ResultInvoiceDto>
        {
            Items = _mapper.Map<List<ResultInvoiceDto>>(items),
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        });
    }

    public async Task<GetByIdInvoiceDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var invoice = await _db.Invoices.AsNoTracking()
            .Include(i => i.Lines.OrderBy(l => l.Id))
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        return invoice is null ? null : _mapper.Map<GetByIdInvoiceDto>(invoice);
    }
}
