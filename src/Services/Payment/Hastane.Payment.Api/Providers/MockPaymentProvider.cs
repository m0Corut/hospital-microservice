namespace Hastane.Payment.Api.Providers;

// SAHTE sağlayıcı: gerçek para hareketi yok. Test kartlarıyla başarı/red senaryosu üretir.
//   4000000000000002 -> "Kart reddedildi"     4000000000009995 -> "Yetersiz bakiye"     diğer geçerli kartlar -> başarı
public class MockPaymentProvider : IPaymentProvider
{
    public const string DeclinedCard = "4000000000000002";
    public const string InsufficientFundsCard = "4000000000009995";

    public Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        var number = request.Card.Number;
        var brand = CardValidator.BrandOf(number);
        var last4 = CardValidator.Last4(number);

        var failure = number switch
        {
            DeclinedCard => "Kart reddedildi",
            InsufficientFundsCard => "Yetersiz bakiye",
            _ => null,
        };

        var result = failure is null
            ? new ChargeResult(true, brand, last4, NewReference(), null)
            : new ChargeResult(false, brand, last4, null, failure);
        return Task.FromResult(result);
    }

    public Task<RefundResult> RefundAsync(string providerReference, decimal amount, string currency, CancellationToken ct)
        => Task.FromResult(new RefundResult(true, null));

    private static string NewReference() => "mock_" + Guid.NewGuid().ToString("N");
}
