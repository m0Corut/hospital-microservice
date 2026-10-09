namespace Hastane.Payment.Api.Entities;

public enum PaymentFailureKind
{
    Declined,        // sağlayıcı kartı reddetti -> 402
    ProviderError,   // sağlayıcıya ulaşılamadı/istisna -> 502
    Duplicate,       // aynı randevu zaten ödenmiş, yarışı kaybetti, ücret iade edildi -> 409
    RecordingFailed, // para çekildi ama kayıt yazılamadı, ücret iade edildi -> 502
}
