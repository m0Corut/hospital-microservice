using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.AdjustStock;

public sealed class AdjustStockValidator : IValidator<AdjustStockCommand>
{
    public const int MaxDelta = 1_000_000;

    public IEnumerable<ValidationFailure> Validate(AdjustStockCommand c)
    {
        var failures = new List<ValidationFailure>();
        if (c.MedicineId == Guid.Empty) failures.Add(new("MedicineId", "İlaç kimliği gerekli."));
        if (c.Delta == 0) failures.Add(new("Delta", "Değişim 0 olamaz."));
        else if (Math.Abs((long)c.Delta) > MaxDelta) failures.Add(new("Delta", $"Değişim en çok {MaxDelta} olabilir."));
        Rules.Required(failures, "Reason", "Neden", c.Reason, 200);
        return failures;
    }
}
