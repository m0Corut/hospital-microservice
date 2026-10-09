using Hastane.Payment.Api.Dtos;
using Hastane.Payment.Api.Mapping;
using Hastane.Payment.Api.Providers;
using Hastane.Payment.Api.Services;
using Mapster;
using PaymentEntity = Hastane.Payment.Api.Entities.Payment;

namespace Hastane.Payment.Tests;

public class CardValidatorTests
{
    [Theory]
    [InlineData("4242424242424242")]
    [InlineData("5555555555554444")]
    [InlineData("378282246310005")]
    [InlineData("4000000000000002")]
    [InlineData("4000000000009995")]
    public void Luhn_AcceptsValidNumbers(string number) => Assert.True(CardValidator.PassesLuhn(number));

    [Theory]
    [InlineData("4242424242424241")]
    [InlineData("1234567812345678")]
    [InlineData("4242 4242 4242 4242")]
    [InlineData("abcdabcdabcdabcd")]
    [InlineData("424242")]
    [InlineData("")]
    public void Luhn_RejectsInvalidNumbers(string number) => Assert.False(CardValidator.PassesLuhn(number));

    [Theory]
    [InlineData("4111111111111111", "Visa")]
    [InlineData("5555555555554444", "Mastercard")]
    [InlineData("378282246310005", "Amex")]
    [InlineData("6011111111111117", "Unknown")]
    public void Brand_FromFirstDigit(string number, string brand) => Assert.Equal(brand, CardValidator.BrandOf(number));

    [Fact]
    public void Expiry_CardIsValidThroughEndOfExpiryMonth()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(CardValidator.IsExpired(10, 2026, now)); // bu ayın sonuna kadar geçerli
        Assert.True(CardValidator.IsExpired(9, 2026, now));
        Assert.False(CardValidator.IsExpired(1, 2030, now));
    }

    [Theory]
    [InlineData("123", true)]
    [InlineData("1234", true)]
    [InlineData("12", false)]
    [InlineData("12345", false)]
    [InlineData("12a", false)]
    public void Cvv_ThreeOrFourDigits(string cvv, bool valid) => Assert.Equal(valid, CardValidator.IsValidCvv(cvv));

    [Fact]
    public void ErrorMessages_NeverContainCardData()
    {
        var error = CardValidator.Validate("4242424242424241", 12, 2099, "987", DateTime.UtcNow);
        Assert.NotNull(error);
        Assert.DoesNotContain("4242", error);
        Assert.DoesNotContain("987", error);
    }
}

public class MockPaymentProviderTests
{
    private static ChargeRequest Request(string number) => new(10m, "TRY", new CardDetails
    {
        Number = number,
        ExpMonth = 12,
        ExpYear = 2099,
        Cvv = "123",
        HolderName = "X",
    });

    [Fact]
    public async Task Charge_Success_HasMockReference_AndBrandAndLast4()
    {
        var result = await new MockPaymentProvider().ChargeAsync(Request("4242424242424242"), TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.StartsWith("mock_", result.ProviderReference);
        Assert.Equal("Visa", result.CardBrand);
        Assert.Equal("4242", result.CardLast4);
    }

    [Theory]
    [InlineData("4000000000000002", "Kart reddedildi")]
    [InlineData("4000000000009995", "Yetersiz bakiye")]
    public async Task Charge_TestDeclineCards(string number, string reason)
    {
        var result = await new MockPaymentProvider().ChargeAsync(Request(number), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal(reason, result.FailureReason);
        Assert.Null(result.ProviderReference);
    }

    [Fact]
    public void CardDetails_ToString_IsMasked()
        => Assert.DoesNotContain("4242", Request("4242424242424242").Card.ToString());
}

public class InvoiceCalculatorTests
{
    [Fact]
    public void TaxInclusive_110At10_Gives100And10()
    {
        var (subtotal, tax) = InvoiceCalculator.SplitTaxInclusive(110.00m, 10m);
        Assert.Equal(100.00m, subtotal);
        Assert.Equal(10.00m, tax);
    }

    [Fact]
    public void TaxInclusive_RoundingCase_100At10()
    {
        // 100 / 1.10 = 90.909090... -> 90.91; KDV = 100 - 90.91 = 9.09
        var (subtotal, tax) = InvoiceCalculator.SplitTaxInclusive(100.00m, 10m);
        Assert.Equal(90.91m, subtotal);
        Assert.Equal(9.09m, tax);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(18)]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(0)]
    public void TaxInclusive_SubtotalPlusTax_AlwaysEqualsAmount(int percent)
    {
        for (var cents = 1; cents <= 20000; cents++)
        {
            var amount = cents / 100m;
            var (subtotal, tax) = InvoiceCalculator.SplitTaxInclusive(amount, percent);
            Assert.Equal(amount, subtotal + tax);
        }
    }

    [Theory]
    [InlineData("1000000.00")]
    [InlineData("999999.99")]
    [InlineData("0.01")]
    public void TaxInclusive_LargeAndTinyAmounts_SumEqualsAmount(string text)
    {
        var amount = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var (subtotal, tax) = InvoiceCalculator.SplitTaxInclusive(amount, 10m);
        Assert.Equal(amount, subtotal + tax);
    }

    [Fact]
    public void InvoiceNumber_Format() => Assert.Equal("INV-2026-000123", InvoiceCalculator.FormatNumber(2026, 123));
}

public class IdempotencyKeyRulesTests
{
    [Theory]
    [InlineData("0197a000-0000-7000-8000-000000000abc", true)] // UUID
    [InlineData("abcdefghijklmnop", true)]                      // tam 16
    [InlineData("abcdefghijklmno", false)]                      // 15
    [InlineData("1", false)]
    [InlineData("abcdefghijklmnop qrstuvwx", false)]            // boşluk
    [InlineData("abcdefghijklmnop!qrstuvwx", false)]            // geçersiz karakter
    [InlineData("abcdefghijklmnopçğ", false)]
    [InlineData(null, false)]
    public void Rules(string? key, bool valid) => Assert.Equal(valid, IdempotencyKeyRules.IsValid(key));

    [Fact]
    public void Rules_Length100Ok_101Rejected()
    {
        Assert.True(IdempotencyKeyRules.IsValid(new string('k', 100)));
        Assert.False(IdempotencyKeyRules.IsValid(new string('k', 101)));
    }
}

public class RequestFingerprintTests
{
    private static CreatePaymentDto Dto(string patient, string doctor) => new()
    {
        AppointmentId = Guid.Parse("0197a000-0000-7000-8000-000000000abc"),
        PatientId = patient,
        DoctorId = doctor,
        Amount = 10m,
        Currency = "TRY",
    };

    [Fact]
    public void DelimiterInsideFields_DoesNotCollide()
        => Assert.NotEqual(RequestFingerprint.Compute(Dto("a|b", "c")), RequestFingerprint.Compute(Dto("a", "b|c")));

    [Fact]
    public void SameInput_SameHash_AndIgnoresCard()
    {
        var a = Dto("p", "d");
        var b = Dto("p", "d");
        b.Card.Number = "4000000000000002";
        Assert.Equal(RequestFingerprint.Compute(a), RequestFingerprint.Compute(b));
        Assert.Equal(64, RequestFingerprint.Compute(a).Length);
    }
}

public class PaymentMappingConfigTests
{
    private static TypeAdapterConfig StrictConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(PaymentMappingConfig).Assembly);
        config.RequireExplicitMapping = true;
        config.RequireDestinationMemberSource = true;
        return config;
    }

    [Fact]
    public void Configuration_Compiles() => StrictConfig().Compile();

    [Fact]
    public void Payment_To_ResultDto_MapsStatusAsText_AndHasNoInvoiceWhenMissing()
    {
        var dto = new PaymentEntity
        {
            Id = Guid.CreateVersion7(),
            Status = Api.Entities.PaymentStatus.Failed,
            Amount = 5m,
            Currency = "TRY",
        }.Adapt<PaymentResultDto>(StrictConfig());

        Assert.Equal("Failed", dto.Status);
        Assert.Null(dto.InvoiceId);
        Assert.Null(dto.InvoiceNumber);
    }
}
