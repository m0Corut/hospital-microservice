namespace Hastane.Payment.Api.Services;

// Ödeme tutarı KDV DAHİLDİR. Önce KDV'siz tutar (Subtotal) bulunur ve yuvarlanır; KDV (Tax) ise
// "Amount - Subtotal" farkıdır. İkisini ayrı ayrı yuvarlasaydık toplam bir kuruş şaşabilirdi;
// çıkarma yoluyla Subtotal + Tax her zaman tam olarak Amount eder.
public static class InvoiceCalculator
{
    public static (decimal Subtotal, decimal TaxAmount) SplitTaxInclusive(decimal amount, decimal taxPercent)
    {
        var subtotal = Math.Round(amount / (1 + taxPercent / 100m), 2, MidpointRounding.AwayFromZero);
        return (subtotal, amount - subtotal);
    }

    public static string FormatNumber(int year, int sequence) => $"INV-{year}-{sequence:D6}";
}
