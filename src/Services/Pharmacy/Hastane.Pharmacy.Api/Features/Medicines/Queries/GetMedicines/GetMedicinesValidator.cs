using Hastane.Pharmacy.Api.Entities;
using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMedicines;

public sealed class GetMedicinesValidator : IValidator<GetMedicinesQuery>
{
    public IEnumerable<ValidationFailure> Validate(GetMedicinesQuery q)
    {
        var failures = new List<ValidationFailure>();
        Rules.Paging(failures, q.Page, q.PageSize);
        if (!string.IsNullOrWhiteSpace(q.Form) && !TryParseForm(q.Form, out _))
            failures.Add(new("Form", "Geçersiz ilaç formu."));
        if (q.Search is { Length: > 100 })
            failures.Add(new("Search", "Arama metni en çok 100 karakter olabilir."));
        return failures;
    }

    // Sayısal metin ("1") kabul edilmez: yalnız ad.
    public static bool TryParseForm(string? text, out MedicineForm form)
    {
        form = default;
        var trimmed = text?.Trim();
        return !string.IsNullOrEmpty(trimmed)
               && !char.IsDigit(trimmed[0])
               && Enum.TryParse(trimmed, ignoreCase: true, out form)
               && Enum.IsDefined(form);
    }
}
