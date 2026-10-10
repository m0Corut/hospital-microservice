using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hastane.DevSeed;

// Status 0 = ağ hatası (Gateway kapalı vb.). Error: ProblemDetails'tan çıkarılmış okunur metin.
public sealed record ApiResult<T>(int Status, T? Value, string? Error)
{
    public bool Ok => Status is >= 200 and < 300;
    public bool IsConflict => Status == 409;

    public override string ToString() => $"HTTP {Status}: {Error}";
}

public sealed record Nothing;

// Yanıt modelleri: yalnız araç için gereken alanlar. Enum'lar sunucuda metin olarak döner (JsonStringEnumConverter).
public sealed record UserInfo(string Id, string Email, List<string> Roles, string? DoctorId);
public sealed record LoginResult(string AccessToken, UserInfo User);
public sealed record BranchInfo(string Id, string Name);
public sealed record DoctorListItem(string Id, string FirstName, string LastName, string BranchId);
public sealed record DoctorDetail(string Id, string BranchId, decimal? MinFee, string? Currency);
public sealed record SlotInfo(DateTime Start, DateTime End);
public sealed record AppointmentInfo(Guid Id, string DoctorId, string PatientId, string BranchId, DateTime AppointmentDate, string Status);
public sealed record PaymentInfo(Guid Id, string Status);
public sealed record PrescriptionInfo(Guid Id, Guid AppointmentId);
public sealed record ReviewInfo(string Id, string? Comment);

// Gateway üzerinden tüm çağrılar; yol önekini (gateway/) ve Bearer başlığını burada ekler.
public sealed class GatewayApi(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // --- Identity ---
    public Task<ApiResult<LoginResult>> LoginAsync(string email, string password, CancellationToken ct)
        => SendAsync<LoginResult>(HttpMethod.Post, "auth/login", new { email, password }, null, null, ct);

    public Task<ApiResult<UserInfo>> RegisterAsync(string firstName, string lastName, string email, string password, CancellationToken ct)
        => SendAsync<UserInfo>(HttpMethod.Post, "auth/register", new { firstName, lastName, email, password }, null, null, ct);

    public Task<ApiResult<UserInfo>> AddRoleAsync(string adminToken, string userId, string role, CancellationToken ct)
        => SendAsync<UserInfo>(HttpMethod.Post, $"users/{Uri.EscapeDataString(userId)}/roles", new { role }, adminToken, null, ct);

    public Task<ApiResult<UserInfo>> RemoveRoleAsync(string adminToken, string userId, string role, CancellationToken ct)
        => SendAsync<UserInfo>(HttpMethod.Delete, $"users/{Uri.EscapeDataString(userId)}/roles/{Uri.EscapeDataString(role)}", null, adminToken, null, ct);

    public Task<ApiResult<UserInfo>> LinkDoctorAsync(string adminToken, string userId, string doctorId, CancellationToken ct)
        => SendAsync<UserInfo>(HttpMethod.Put, $"users/{Uri.EscapeDataString(userId)}/doctor-link", new { doctorId }, adminToken, null, ct);

    // --- Branch ---
    public Task<ApiResult<List<BranchInfo>>> ListBranchesAsync(CancellationToken ct)
        => SendAsync<List<BranchInfo>>(HttpMethod.Get, "branches", null, null, null, ct);

    public Task<ApiResult<BranchInfo>> CreateBranchAsync(string adminToken, object body, CancellationToken ct)
        => SendAsync<BranchInfo>(HttpMethod.Post, "branches", body, adminToken, null, ct);

    // --- Doctor ---
    public Task<ApiResult<List<DoctorListItem>>> ListDoctorsAsync(CancellationToken ct)
        => SendAsync<List<DoctorListItem>>(HttpMethod.Get, "doctors", null, null, null, ct);

    public Task<ApiResult<DoctorDetail>> GetDoctorAsync(string token, string doctorId, CancellationToken ct)
        => SendAsync<DoctorDetail>(HttpMethod.Get, $"doctors/{Uri.EscapeDataString(doctorId)}", null, token, null, ct);

    public Task<ApiResult<DoctorListItem>> CreateDoctorAsync(string adminToken, object body, CancellationToken ct)
        => SendAsync<DoctorListItem>(HttpMethod.Post, "doctors", body, adminToken, null, ct);

    // --- Appointment ---
    public Task<ApiResult<List<SlotInfo>>> GetAvailabilityAsync(string token, string doctorId, DateOnly date, CancellationToken ct)
        => SendAsync<List<SlotInfo>>(HttpMethod.Get, $"appointments/availability?doctorId={Uri.EscapeDataString(doctorId)}&date={date:yyyy-MM-dd}", null, token, null, ct);

    public Task<ApiResult<List<AppointmentInfo>>> ListMyAppointmentsAsync(string token, CancellationToken ct)
        => SendAsync<List<AppointmentInfo>>(HttpMethod.Get, "appointments", null, token, null, ct);

    public Task<ApiResult<AppointmentInfo>> CreateAppointmentAsync(string patientToken, string doctorId, string branchId, DateTime startUtc, string? note, CancellationToken ct)
        => SendAsync<AppointmentInfo>(HttpMethod.Post, "appointments",
            new { doctorId, branchId, appointmentDate = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), note }, patientToken, null, ct);

    public Task<ApiResult<AppointmentInfo>> SetStatusAsync(string token, Guid appointmentId, string status, CancellationToken ct)
        => SendAsync<AppointmentInfo>(HttpMethod.Patch, $"appointments/{appointmentId}/status", new { status }, token, null, ct);

    // --- Payment ---
    public Task<ApiResult<PaymentInfo>> CreatePaymentAsync(string patientToken, string idempotencyKey, object body, CancellationToken ct)
        => SendAsync<PaymentInfo>(HttpMethod.Post, "payments", body, patientToken, idempotencyKey, ct);

    // --- Prescription ---
    public Task<ApiResult<List<PrescriptionInfo>>> ListPrescriptionsByAppointmentAsync(string doctorToken, Guid appointmentId, CancellationToken ct)
        => SendAsync<List<PrescriptionInfo>>(HttpMethod.Get, $"prescriptions?appointmentId={appointmentId}", null, doctorToken, null, ct);

    public Task<ApiResult<Nothing>> CreatePrescriptionAsync(string doctorToken, object body, CancellationToken ct)
        => SendAsync<Nothing>(HttpMethod.Post, "prescriptions", body, doctorToken, null, ct);

    // --- Review ---
    public Task<ApiResult<List<ReviewInfo>>> ListReviewsByDoctorAsync(string doctorId, CancellationToken ct)
        => SendAsync<List<ReviewInfo>>(HttpMethod.Get, $"reviews?doctorId={Uri.EscapeDataString(doctorId)}", null, null, null, ct);

    public Task<ApiResult<Nothing>> CreateReviewAsync(string patientToken, string doctorId, int rating, string comment, CancellationToken ct)
        => SendAsync<Nothing>(HttpMethod.Post, "reviews", new { doctorId, rating, comment }, patientToken, null, ct);

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method, string path, object? body, string? token, string? idempotencyKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, "gateway/" + path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), null, Json);

        try
        {
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
                return new ApiResult<T>(status, default, DescribeProblem(text, response.ReasonPhrase));

            var value = text.Length > 0 ? JsonSerializer.Deserialize<T>(text, Json) : default;
            return new ApiResult<T>(status, value, null);
        }
        catch (HttpRequestException ex)
        {
            return new ApiResult<T>(0, default, $"Gateway'e ulaşılamadı: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return new ApiResult<T>(0, default, $"Zaman aşımı: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return new ApiResult<T>(0, default, $"Yanıt çözülemedi: {ex.Message}");
        }
    }

    // ProblemDetails: title + detail; ValidationProblem ise errors sözlüğü de eklenir. JSON değilse ham metnin başı.
    private static string DescribeProblem(string body, string? reason)
    {
        if (string.IsNullOrWhiteSpace(body)) return reason ?? "(boş yanıt)";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return Clip(body);
            var sb = new StringBuilder();
            if (doc.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                sb.Append(title.GetString());
            if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                sb.Append(sb.Length > 0 ? " - " : "").Append(detail.GetString());
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in errors.EnumerateObject())
                {
                    var messages = prop.Value.ValueKind == JsonValueKind.Array
                        ? string.Join("; ", prop.Value.EnumerateArray().Select(e => e.ToString()))
                        : prop.Value.ToString();
                    sb.Append(" [").Append(prop.Name).Append(": ").Append(messages).Append(']');
                }
            }

            return sb.Length > 0 ? sb.ToString() : Clip(body);
        }
        catch (JsonException)
        {
            return Clip(body);
        }
    }

    private static string Clip(string s) => s.Length <= 300 ? s : s[..300] + "...";
}
