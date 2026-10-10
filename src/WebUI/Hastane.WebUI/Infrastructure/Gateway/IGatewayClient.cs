namespace Hastane.WebUI.Infrastructure.Gateway;

// Tek HTTP kapısı. Alan istemcileri (IBranchClient...) bunu kullanır; controller doğrudan çağırmaz.
// Yol her zaman "/gateway/..." ile başlar. Gövde nesnesi camelCase JSON'a çevrilir.
public interface IGatewayClient
{
    Task<ApiResult<T>> GetAsync<T>(string path, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult<T>> PostAsync<T>(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult<T>> PostAsync<T>(string path, object? body, IReadOnlyDictionary<string, string> headers, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);
    Task<ApiResult> PostAsync(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult<T>> PutAsync<T>(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult> PutAsync(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult> PatchAsync(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult> DeleteAsync(string path, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    // multipart/form-data PUT (tek dosya alanı). Dosya diske yazılmadan akıtılır; ad çağıran tarafından sabit verilir
    // (kullanıcının dosya adı üst servise gitmez).
    Task<ApiResult<T>> PutFileAsync<T>(string path, string fieldName, string fileName, string contentType, Stream content,
        ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    // Gövdesini akıtan GET (görsel vekili için). Dönen nesne Dispose edilmeden akış kapanmaz.
    Task<GatewayStream> GetStreamAsync(string path, CancellationToken ct = default);
}

// GetStreamAsync sonucu: bağlantı kesilirse / servis kapalıysa StatusCode 503, Content null.
public sealed class GatewayStream(int statusCode, string? contentType, Stream? content, IDisposable? owner) : IDisposable
{
    public int StatusCode { get; } = statusCode;
    public string? ContentType { get; } = contentType;
    public Stream? Content { get; } = content;
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public void Dispose()
    {
        Content?.Dispose();
        owner?.Dispose();
    }
}
