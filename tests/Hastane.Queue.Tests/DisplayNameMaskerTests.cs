using Hastane.Queue.Api.Services;

namespace Hastane.Queue.Tests;

public class DisplayNameMaskerTests
{
    [Theory]
    [InlineData("Can Yılmaz", "Can Y.")]
    [InlineData("  Can   Yılmaz  ", "Can Y.")]
    [InlineData("Ahmet Can Yılmaz", "Ahmet Y.")]
    [InlineData("Zeynep Kaya-Demir", "Zeynep K.")]
    [InlineData("Ayşe İpek", "Ayşe İ.")]
    [InlineData("Ayşe ipek", "Ayşe İ.")]   // Türkçe büyütme: i -> İ
    [InlineData("Can", "Can")]
    [InlineData("", "Hasta")]
    [InlineData("   ", "Hasta")]
    [InlineData(null, "Hasta")]
    public void Mask_KeepsFirstNameAndSurnameInitialOnly(string? input, string expected)
        => Assert.Equal(expected, DisplayNameMasker.Mask(input));

    [Fact]
    public void Mask_NeverContainsTheSurname()
        => Assert.DoesNotContain("ılmaz", DisplayNameMasker.Mask("Can Yılmaz"));

    [Fact]
    public void Mask_TruncatesVeryLongFirstName()
        => Assert.True(DisplayNameMasker.Mask(new string('a', 500) + " Soyad").Length <= 64);
}
