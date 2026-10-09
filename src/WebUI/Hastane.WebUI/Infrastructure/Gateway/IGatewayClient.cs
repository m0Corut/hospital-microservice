namespace Hastane.WebUI.Infrastructure.Gateway;

// Tek HTTP kapısı. Alan istemcileri (IBranchClient...) bunu kullanır; controller doğrudan çağırmaz.
// Yol her zaman "/gateway/..." ile başlar. Gövde nesnesi camelCase JSON'a çevrilir.
public interface IGatewayClient
{
    Task<ApiResult<T>> GetAsync<T>(string path, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult<T>> PostAsync<T>(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult> PostAsync(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult<T>> PutAsync<T>(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult> PutAsync(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult> PatchAsync(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);

    Task<ApiResult> DeleteAsync(string path, ApiFailures handle = ApiFailures.None, CancellationToken ct = default);
}
