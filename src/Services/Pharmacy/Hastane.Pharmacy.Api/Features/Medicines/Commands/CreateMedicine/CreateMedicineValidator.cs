using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.CreateMedicine;

public sealed class CreateMedicineValidator : IValidator<CreateMedicineCommand>
{
    public IEnumerable<ValidationFailure> Validate(CreateMedicineCommand c)
    {
        var failures = new List<ValidationFailure>();
        Rules.Required(failures, "Name", "İlaç adı", c.Name, 150);
        Rules.Required(failures, "ActiveIngredient", "Etken madde", c.ActiveIngredient, 150);
        Rules.Form(failures, c.Form);
        Rules.Required(failures, "Strength", "Doz", c.Strength, 50);
        Rules.Barcode(failures, c.Barcode);
        Rules.Price(failures, c.Price);
        Rules.NonNegative(failures, "Stock", "Stok", c.Stock);
        Rules.NonNegative(failures, "MinStock", "Asgari stok", c.MinStock);
        return failures;
    }
}
