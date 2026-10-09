namespace Hastane.Payment.Api.Providers;

// Ödeme sağlayıcısı sözleşmesi. Servis yalnız bu arayüzü bilir; sahte (mock) sağlayıcı yarın gerçek bir
// sağlayıcıyla (iyzico, Stripe...) Program.cs'teki tek satırla değişir, servis kodu değişmez.
public interface IPaymentProvider
{
    Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct);
    Task<RefundResult> RefundAsync(string providerReference, decimal amount, string currency, CancellationToken ct);
}

// Kart bilgisi yalnız bu çağrı süresince bellekte durur. ToString maskeli: yanlışlıkla loglansa sızmaz.
public sealed class CardDetails
{
    public required string Number { get; init; }
    public required int ExpMonth { get; init; }
    public required int ExpYear { get; init; }
    public required string Cvv { get; init; }
    public required string HolderName { get; init; }

    public override string ToString() => "CardDetails(***)";
}

public sealed record ChargeRequest(decimal Amount, string Currency, CardDetails Card);

// CardBrand / CardLast4 reddedilen denemede de dolu gelir (denetim izi için).
public sealed record ChargeResult(bool Success, string CardBrand, string CardLast4, string? ProviderReference, string? FailureReason);

public sealed record RefundResult(bool Success, string? FailureReason);
