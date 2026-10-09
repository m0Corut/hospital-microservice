using System.Diagnostics.CodeAnalysis;

namespace Hastane.WebUI.Infrastructure.Gateway;

public enum ApiResultKind
{
    Success,
    // 400 / 422: form hataları (ProblemDetails.Errors) var; ModelState'e aktarılır.
    ValidationFailed,
    // 409: iş kuralı çakışması (ör. e-posta kayıtlı).
    Conflict,
    // 423: hesap kilitli.
    Locked,
    // Aşağıdaki dördü varsayılan olarak GatewayException fırlatır (bkz. ApiFailures).
    Unauthorized,
    Forbidden,
    NotFound,
    Unavailable,
    // Diğer 4xx (408, 429...).
    OtherError,
}

// Hangi "altyapı" hatalarını istemci fırlatmasın, çağıran kendisi ele alsın. Varsayılan None: ekranlar bunları
// düşünmez, GatewayExceptionFilter 401 → girişe, 403/404/503 → hata sayfasına çevirir.
[Flags]
public enum ApiFailures
{
    None = 0,
    Unauthorized = 1,
    Forbidden = 2,
    NotFound = 4,
    Unavailable = 8,
    All = Unauthorized | Forbidden | NotFound | Unavailable,
}

// Sunucunun ProblemDetails / ValidationProblemDetails gövdesinin WebUI'daki karşılığı.
public sealed record ApiProblem(
    int Status,
    string? Title,
    string? Detail,
    IReadOnlyDictionary<string, string[]> Errors);

// Bir API çağrısının sonucu. Başarıda Value dolu, değilse Problem (varsa) dolu.
public class ApiResult
{
    public ApiResultKind Kind { get; init; }
    public int StatusCode { get; init; }
    public ApiProblem? Problem { get; init; }

    public bool IsSuccess => Kind == ApiResultKind.Success;
}

public sealed class ApiResult<T> : ApiResult
{
    public T? Value { get; init; }

    // if (result.TryGetValue(out var v)) { ... } : başarıda true ve değer boş değil.
    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = Value;
        return IsSuccess && value is not null;
    }
}

// Gövdesiz çağrılar (204 vb.) için yer tutucu tip.
public readonly record struct Unit;
