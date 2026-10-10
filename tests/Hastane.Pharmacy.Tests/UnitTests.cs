using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Entities;
using Hastane.Pharmacy.Api.Features.Dispensing.Commands.DispensePrescription;
using Hastane.Pharmacy.Api.Features.Medicines.Commands.AdjustStock;
using Hastane.Pharmacy.Api.Features.Medicines.Commands.CreateMedicine;
using Hastane.Pharmacy.Api.Features.Medicines.Queries.GetMedicines;

namespace Hastane.Pharmacy.Tests;

// Saf mantık testleri: Docker ve veritabanı yok.
public class TurkishTextTests
{
    [Theory]
    [InlineData("Ağrı Kesici", "agri kesici")]
    [InlineData("AĞRI KESİCİ", "agri kesici")]
    [InlineData("agri kesici", "agri kesici")]
    [InlineData("PARASETAMOL", "parasetamol")]
    [InlineData("Şurup ÇÖZÜCÜ", "surup cozucu")]
    [InlineData("  Aspirin   100  mg ", "aspirin 100 mg")]
    [InlineData("ISIRGAN", "isirgan")]
    [InlineData("İbuprofen", "ibuprofen")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Fold_IgnoresCaseTurkishLettersAndExtraSpaces(string? input, string expected)
        => Assert.Equal(expected, TurkishText.Fold(input));

    [Fact]
    public void Fold_TreatsAllSpellingsAsTheSameMedicine()
    {
        var spellings = new[] { "Ağrı Kesici", "AĞRI KESİCİ", "agri kesici", "AGRI KESICI", "ağrı  kesici " };
        Assert.Single(spellings.Select(TurkishText.Fold).Distinct());
    }
}

public class ValidatorTests
{
    private static CreateMedicineCommand ValidCreate() => new(
        "Parol", "Parasetamol", MedicineForm.Tablet, "500 mg", "8690000000001", 25.50m, 10, 5, false);

    private static string[] Properties(IEnumerable<Hastane.Pharmacy.Api.Validation.ValidationFailure> failures)
        => failures.Select(f => f.Property).ToArray();

    [Fact]
    public void CreateMedicine_ValidCommand_HasNoFailures()
        => Assert.Empty(new CreateMedicineValidator().Validate(ValidCreate()));

    [Fact]
    public void CreateMedicine_ReportsEachBrokenField()
    {
        var bad = new CreateMedicineCommand(
            new string('x', 151), "", (MedicineForm)99, new string('y', 51), "bar kod", -1m, -3, -2, false);

        var properties = Properties(new CreateMedicineValidator().Validate(bad));

        Assert.Contains("Name", properties);
        Assert.Contains("ActiveIngredient", properties);
        Assert.Contains("Form", properties);
        Assert.Contains("Strength", properties);
        Assert.Contains("Barcode", properties);
        Assert.Contains("Price", properties);
        Assert.Contains("Stock", properties);
        Assert.Contains("MinStock", properties);
    }

    [Theory]
    [InlineData(10.123)]
    [InlineData(-0.01)]
    [InlineData(10000000)]
    public void CreateMedicine_RejectsBadPrices(double price)
        => Assert.Contains("Price", Properties(new CreateMedicineValidator().Validate(ValidCreate() with { Price = (decimal)price })));

    [Fact]
    public void CreateMedicine_BarcodeLongerThan20_Rejected()
        => Assert.Contains("Barcode", Properties(new CreateMedicineValidator().Validate(ValidCreate() with { Barcode = new string('1', 21) })));

    [Fact]
    public void CreateMedicine_ExactlyMaxLengths_Accepted()
    {
        var edge = ValidCreate() with { Name = new string('a', 150), ActiveIngredient = new string('b', 150), Strength = new string('c', 50), Barcode = new string('1', 20) };
        Assert.Empty(new CreateMedicineValidator().Validate(edge));
    }

    [Theory]
    [InlineData(0, "neden")]
    [InlineData(5, "")]
    [InlineData(5, "   ")]
    [InlineData(1_000_001, "neden")]
    [InlineData(-1_000_001, "neden")]
    public void AdjustStock_RejectsZeroDeltaHugeDeltaAndMissingReason(int delta, string reason)
        => Assert.NotEmpty(new AdjustStockValidator().Validate(new AdjustStockCommand(Guid.NewGuid(), delta, reason)));

    [Fact]
    public void AdjustStock_ValidCommand_Passes()
        => Assert.Empty(new AdjustStockValidator().Validate(new AdjustStockCommand(Guid.NewGuid(), -3, "fire")));

    [Theory]
    [InlineData("Tablet", true)]
    [InlineData("tablet", true)]
    [InlineData("Syrup", true)]
    [InlineData("1", false)]
    [InlineData("Macun", false)]
    [InlineData("", false)]
    public void GetMedicines_FormFilterMustBeAKnownName(string form, bool valid)
    {
        var failures = new GetMedicinesValidator().Validate(new GetMedicinesQuery(Form: form));
        // Boş form "süzgeç yok" demek: hata sayılmaz.
        Assert.Equal(valid || form == "", !failures.Any(f => f.Property == "Form"));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void GetMedicines_RejectsBadPaging(int page, int pageSize)
        => Assert.NotEmpty(new GetMedicinesValidator().Validate(new GetMedicinesQuery(Page: page, PageSize: pageSize)));

    [Fact]
    public void DispensePrescription_EmptyGuidRejected()
        => Assert.Single(new DispensePrescriptionValidator().Validate(new DispensePrescriptionCommand(Guid.Empty)));
}
