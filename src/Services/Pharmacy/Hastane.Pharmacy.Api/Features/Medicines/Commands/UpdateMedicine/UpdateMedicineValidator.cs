using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Features.Medicines.Commands.UpdateMedicine;

public sealed class UpdateMedicineValidator : IValidator<UpdateMedicineCommand>
{
    public IEnumerable<ValidationFailure> Validate(UpdateMedicineCommand c)
    {
        var failures = new List<ValidationFailure>();
        if (c.Id == Guid.Empty) failures.Add(new("Id", "İlaç kimliği gerekli."));
        Rules.Required(failures, "Name", "İlaç adı", c.Name, 150);
        Rules.Required(failures, "ActiveIngredient", "Etken madde", c.ActiveIngredient, 150);
        Rules.Form(failures, c.Form);
        Rules.Required(failures, "Strength", "Doz", c.Strength, 50);
        Rules.Barcode(failures, c.Barcode);
        Rules.Price(failures, c.Price);
        Rules.NonNegative(failures, "MinStock", "Asgari stok", c.MinStock);
        return failures;
    }
}
