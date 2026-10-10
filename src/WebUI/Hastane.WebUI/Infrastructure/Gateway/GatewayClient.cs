using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hastane.WebUI.Infrastructure.Gateway;

public sealed class GatewayClient(HttpClient http, ILogger<GatewayClient> logger) : IGatewayClient
{
    // Sunucu camelCase yazar; okurken büyük/küçük harf farkı önemsiz. Enum'lar metin ("Confirmed") olarak gelir.
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Task<ApiResult<T>> GetAsync<T>(string path, ApiFailures handle = ApiFailures.None, CancellationToken ct = default)
        => SendAsync<T>(HttpMethod.Get, path, null, handle, ct);

    public Task<ApiResult<T>> PostAsync<T>(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default)
        => SendAsync<T>(HttpMethod.Post, path, body, handle, ct);

    public Task<ApiResult<T>> PostAsync<T>(string path, object? body, IReadOnlyDictionary<string, string> headers,
        ApiFailures handle = ApiFailures.None, CancellationToken ct = default)
        => SendAsync<T>(HttpMethod.Post, path, body, handle, ct, headers);

    public async Task<ApiResult> PostAsync(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default)
        => await SendAsync<Unit>(HttpMethod.Post, path, body, handle, ct);

    public Task<ApiResult<T>> PutAsync<T>(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default)
        => SendAsync<T>(HttpMethod.Put, path, body, handle, ct);

    public async Task<ApiResult> PutAsync(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default)
        => await SendAsync<Unit>(HttpMethod.Put, path, body, handle, ct);

    public async Task<ApiResult> PatchAsync(string path, object? body, ApiFailures handle = ApiFailures.None, CancellationToken ct = default)
        => await SendAsync<Unit>(HttpMethod.Patch, path, body, handle, ct);

    public async Task<ApiResult> DeleteAsync(string path, ApiFailures handle = ApiFailures.None, CancellationToken ct = default)
        => await SendAsync<Unit>(HttpMethod.Delete, path, null, handle, ct);

    public async Task<ApiResult<T>> PutFileAsync<T>(string path, string fieldName, string fileName, string contentType, Stream content,
        ApiFailures handle = ApiFailures.None, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, fieldName, fileName);
        return await SendAsync<T>(HttpMethod.Put, path, null, handle, ct, null, form);
    }

    public async Task<GatewayStream> GetStreamAsync(string path, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Gateway'e ulaşılamadı: GET {Path}", path);
            return new GatewayStream((int)HttpStatusCode.ServiceUnavailable, null, null, null);
        }

        var status = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return new GatewayStream(status, null, null, null);
        }

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return new GatewayStream(status, response.Content.Headers.ContentType?.MediaType, stream, response);
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, ApiFailures handle, CancellationToken ct,
        IReadOnlyDictionary<string, string>? headers = null, HttpContent? content = null)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(method, path);
            // İsteğe özel başlık (ör. Idempotency-Key) yalnız bu isteğe yazılır, paylaşılan istemciye değil.
            foreach (var (name, value) in headers ?? new Dictionary<string, string>())
                request.Headers.TryAddWithoutValidation(name, value);
            if (content is not null)
                request.Content = content;
            else if (body is not null)
                request.Content = JsonContent.Create(body, body.GetType(), options: Json);
            response = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Bağlantı reddedildi, DNS, zaman aşımı: ayrıntı (adres, yığın) kullanıcıya gitmez, yalnız günlüğe.
            logger.LogWarning(ex, "Gateway'e ulaşılamadı: {Method} {Path}", method, path);
            return Fail<T>(ApiResultKind.Unavailable, (int)HttpStatusCode.ServiceUnavailable, null, handle, "Servis yanıt vermiyor.", ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                T? value = default;
                if (typeof(T) != typeof(Unit) && response.StatusCode != HttpStatusCode.NoContent)
                    value = await ReadAsync<T>(response, ct);
                return new ApiResult<T> { Kind = ApiResultKind.Success, StatusCode = status, Value = value };
            }

            var kind = status switch
            {
                400 or 422 => ApiResultKind.ValidationFailed,
                401 => ApiResultKind.Unauthorized,
                403 => ApiResultKind.Forbidden,
                404 => ApiResultKind.NotFound,
                409 => ApiResultKind.Conflict,
                423 => ApiResultKind.Locked,
                >= 500 => ApiResultKind.Unavailable,
                _ => ApiResultKind.OtherError,
            };

            if (kind == ApiResultKind.Unavailable)
                logger.LogWarning("Gateway {Status} döndü: {Method} {Path}", status, method, path);

            var problem = kind == ApiResultKind.Unavailable ? null : await ReadProblemAsync(response, status, ct);
            return Fail<T>(kind, status, problem, handle, $"{method} {path} → {status}", null);
        }
    }

    private static ApiResult<T> Fail<T>(ApiResultKind kind, int status, ApiProblem? problem, ApiFailures handle, string message, Exception? inner)
    {
        var flag = kind switch
        {
            ApiResultKind.Unauthorized => ApiFailures.Unauthorized,
            ApiResultKind.Forbidden => ApiFailures.Forbidden,
            ApiResultKind.NotFound => ApiFailures.NotFound,
            ApiResultKind.Unavailable => ApiFailures.Unavailable,
            _ => ApiFailures.None,
        };

        // Altyapı hatası ve çağıran "ben ele alırım" demediyse fırlat: ekranlar bunu düşünmek zorunda kalmasın.
        if (flag != ApiFailures.None && !handle.HasFlag(flag))
            throw new GatewayException(kind, status, message, inner);

        return new ApiResult<T> { Kind = kind, StatusCode = status, Problem = problem };
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
        // Sözleşme bozuksa (beklenmeyen gövde) JsonException kasıtlı olarak yukarı çıkar: kullanıcı 500 sayfasını görür.
        => await response.Content.ReadFromJsonAsync<T>(Json, ct);

    private static async Task<ApiProblem?> ReadProblemAsync(HttpResponseMessage response, int status, CancellationToken ct)
    {
        try
        {
            var dto = await response.Content.ReadFromJsonAsync<ProblemDto>(Json, ct);
            if (dto is null) return null;
            return new ApiProblem(dto.Status ?? status, dto.Title, dto.Detail,
                dto.Errors ?? new Dictionary<string, string[]>());
        }
        catch (JsonException)
        {
            return null; // gövde ProblemDetails değil (boş / düz metin)
        }
    }

    private sealed record ProblemDto(int? Status, string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}
