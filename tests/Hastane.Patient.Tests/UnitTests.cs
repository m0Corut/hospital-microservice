using Hastane.Patient.Api.Entities;
using Hastane.Patient.Api.Services;
using Hastane.Patient.Api.Validation;

namespace Hastane.Patient.Tests;

// Veritabanı ve HTTP gerektirmeyen saf mantık.
public class UnitTests
{
    [Theory]
    [InlineData("2000-06-15", "2026-06-14", 25)] // doğum günü yarın: henüz 25
    [InlineData("2000-06-15", "2026-06-15", 26)] // doğum günü bugün: 26
    [InlineData("2000-06-15", "2026-06-16", 26)]
    [InlineData("2026-06-15", "2026-06-15", 0)]  // bugün doğdu
    [InlineData("2024-02-29", "2025-02-28", 1)]  // 29 Şubat doğumlu: artık olmayan yılda 28 Şubat
    [InlineData("2024-02-29", "2025-02-27", 0)]
    [InlineData("1900-01-01", "2026-01-01", 126)]
    public void Age_IsWholeYears(string birth, string today, int expected)
        => Assert.Equal(expected, AgeCalculator.Calculate(DateOnly.Parse(birth), DateOnly.Parse(today)));

    [Fact]
    public void Age_WithoutBirthDate_IsNull()
        => Assert.Null(AgeCalculator.Calculate(null, new DateOnly(2026, 1, 1)));

    [Theory]
    [InlineData(BloodType.APos, "A Rh+")]
    [InlineData(BloodType.ANeg, "A Rh-")]
    [InlineData(BloodType.BPos, "B Rh+")]
    [InlineData(BloodType.BNeg, "B Rh-")]
    [InlineData(BloodType.ABPos, "AB Rh+")]
    [InlineData(BloodType.ABNeg, "AB Rh-")]
    [InlineData(BloodType.ZeroPos, "0 Rh+")]
    [InlineData(BloodType.ZeroNeg, "0 Rh-")]
    public void BloodType_Display(BloodType type, string expected)
        => Assert.Equal(expected, BloodTypes.Display(type));

    [Fact]
    public void BloodType_Display_NullIsNull() => Assert.Null(BloodTypes.Display(null));

    [Fact]
    public void BloodType_HasEightValues_AllWithDisplay()
    {
        var all = Enum.GetValues<BloodType>();
        Assert.Equal(8, all.Length);
        Assert.All(all, t => Assert.NotNull(BloodTypes.Display(t)));
    }

    [Fact]
    public void BirthDate_Rules()
    {
        var attribute = new BirthDateAttribute();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.True(attribute.IsValid(null));
        Assert.True(attribute.IsValid(new DateOnly(1900, 1, 1)));
        Assert.True(attribute.IsValid(today));
        Assert.False(attribute.IsValid(new DateOnly(1899, 12, 31)));
        Assert.False(attribute.IsValid(today.AddDays(2)));
    }
}
