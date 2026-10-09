using Hastane.Payment.Api.Data;
using Hastane.Payment.Api.Dtos;
using Hastane.Shared.Security;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Payment.Api.Services;

public class InvoiceService : IInvoiceService
{
    private readonly PaymentDbContext _db;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _user;

    public InvoiceService(PaymentDbContext db, IMapper mapper, ICurrentUser user)
    {
        _db = db;
        _mapper = mapper;
        _user = user;
    }

    public async Task<OperationResult<PagedResult<ResultInvoiceDto>>> GetAllAsync(InvoiceListQuery query, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 50)
            return OperationResult<PagedResult<ResultInvoiceDto>>.Validation("page >= 1 ve pageSize 1-50 arasında olmalı.");

        var invoices = _db.Invoices.AsNoTracking();

        // Liste filtresi ZORLAMA (IDOR savunması): Admin dışında kapsamı istemci değil token belirler.
        var scope = PaymentAccess.ScopeOf(_user);
        var me = _user.UserId;
        var myDoctor = _user.DoctorId;
        var patientFilter = query.PatientId;
        var doctorFilter = query.DoctorId;
        switch (scope)
        {
            case AccessScope.None:
                return OperationResult<PagedResult<ResultInvoiceDto>>.Ok(new PagedResult<ResultInvoiceDto>
                {
                    Items = [],
                    TotalCount = 0,
                    Page = query.Page,
                    PageSize = query.PageSize,
                });
            case AccessScope.Patient: patientFilter = me; break;
            case AccessScope.Doctor: doctorFilter = myDoctor; break;
            case AccessScope.Either: invoices = invoices.Where(i => i.PatientId == me || i.DoctorId == myDoctor); break;
        }

        if (!string.IsNullOrWhiteSpace(patientFilter)) invoices = invoices.Where(i => i.PatientId == patientFilter);
        if (!string.IsNullOrWhiteSpace(doctorFilter)) invoices = invoices.Where(i => i.DoctorId == doctorFilter);

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
        // Sahibi olmayan için "yok" ile aynı cevap (null -> 404).
        return invoice is null || !PaymentAccess.CanView(_user, invoice.PatientId, invoice.DoctorId)
            ? null : _mapper.Map<GetByIdInvoiceDto>(invoice);
    }
}
