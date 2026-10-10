using Hastane.Pharmacy.Api.Validation;

namespace Hastane.Pharmacy.Api.Behaviors;

// ValidationBehavior geçersiz istekte bunu fırlatır; ValidationExceptionHandler 400 + alan hatalarına çevirir.
// Mesaj yalnız hata sayısını söyler: girdinin KENDİSİ (ilaç adı, barkod...) istisna metnine ya da loga girmez.
public sealed class RequestValidationException(IReadOnlyList<ValidationFailure> failures)
    : Exception($"İstek doğrulamadan geçemedi ({failures.Count} hata).")
{
    public IReadOnlyList<ValidationFailure> Failures { get; } = failures;
}
