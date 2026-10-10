using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMedicines;

// OKUMA modeli: AsNoTracking (değişiklik izleme yok) + doğrudan DTO projeksiyonu (yalnız gereken kolonlar SELECT'e girer).
public sealed class GetMedicinesHandler(PharmacyDbContext db)
    : IRequestHandler<GetMedicinesQuery, OperationResult<PagedResult<MedicineDto>>>
{
    public async Task<OperationResult<PagedResult<MedicineDto>>> Handle(GetMedicinesQuery query, CancellationToken ct)
    {
        var medicines = db.Medicines.AsNoTracking().Where(m => m.IsActive);

        if (GetMedicinesValidator.TryParseForm(query.Form, out var form))
            medicines = medicines.Where(m => m.Form == form);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Ad/etken madde Türkçe harf ve büyük-küçük harf duyarsız; barkod tam eşleşme.
            var folded = TurkishText.Fold(query.Search);
            var barcode = query.Search.Trim();
            medicines = medicines.Where(m =>
                m.NormalizedName.Contains(folded) || m.NormalizedIngredient.Contains(folded) || m.Barcode == barcode);
        }

        var total = await medicines.CountAsync(ct);
        var items = await medicines
            .OrderBy(m => m.NormalizedName).ThenBy(m => m.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(MedicineProjection.Projection)
            .ToListAsync(ct);

        return OperationResult<PagedResult<MedicineDto>>.Ok(new PagedResult<MedicineDto>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        });
    }
}
