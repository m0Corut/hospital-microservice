using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hastane.WebUI.Tests;

/// <summary>Sahte gateway'e giden bir yolun sorgu dizilerini ve Idempotency-Key başlıklarını kaydeder (FakeGateway yalnız yolu tutuyor).</summary>
internal sealed class CallLog
{
    public ConcurrentQueue<string> Queries { get; } = new();
    public ConcurrentQueue<string> Keys { get; } = new();
    public int Count => Queries.Count;
    public string LastQuery => Queries.Last();
}

internal static partial class PublicPatientTestHelpers
{
    public const string DoctorId = "d1";
    public static readonly Guid Appt1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Appt2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Rx1 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Pay1 = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid Inv1 = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public static async Task<HttpClient> LoginAsAsync(this WebUiTestHost host, string role, string name = "Ayşe Yılmaz")
    {
        var client = host.CreateClient();
        await host.LoginAsync(client, WebUiTestHost.NewToken(role, name), roles: [role]);
        return client;
    }

    public static CallLog MapLogged(this FakeGateway gateway, string method, string path, int status, object? body = null)
    {
        var log = new CallLog();
        gateway.Map(method, path, ctx =>
        {
            log.Queries.Enqueue(ctx.Request.QueryString.Value ?? string.Empty);
            log.Keys.Enqueue(ctx.Request.Headers["Idempotency-Key"].ToString());
            ctx.Response.StatusCode = status;
            return body is null ? Task.CompletedTask : ctx.Response.WriteAsJsonAsync(body);
        });
        return log;
    }

    public static IEnumerable<RecordedRequest> Calls(this FakeGateway gateway, string method, string path)
        => gateway.Requests.Where(r => r.Method == method && r.Path == path);

    /// <summary>Antiforgery belirteci herhangi bir sayfadan alınır (belirteç adrese bağlı değil); POST doğrudan hedef adrese gider.</summary>
    public static async Task<HttpResponseMessage> PostAsync(this WebUiTestHost host, HttpClient client, string postPath, Dictionary<string, string>? fields = null)
    {
        host.Gateway.MapBranches("Kardiyoloji");
        var html = await (await client.GetAsync("/Branches")).Content.ReadAsStringAsync();
        if (!html.Contains("__RequestVerificationToken", StringComparison.Ordinal)) // anonim: çıkış formu yok, giriş formu var
            html = await (await client.GetAsync("/Account/Login")).Content.ReadAsStringAsync();
        var form = fields ?? [];
        form["__RequestVerificationToken"] = WebUiTestHost.ExtractAntiforgeryToken(html);
        return await client.PostAsync(postPath, new FormUrlEncodedContent(form));
    }

    public static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement;

    public static string? Location(HttpResponseMessage response) => TestUrls.Relative(response.Headers.Location);

    public static string ExtractIdempotencyKey(string html)
    {
        var match = KeyRegex().Match(html);
        Assert.True(match.Success, "Formda Idempotency anahtarı yok.");
        return match.Groups[1].Value;
    }

    [GeneratedRegex("name=\"IdempotencyKey\" value=\"([^\"]+)\"")]
    private static partial Regex KeyRegex();

    // ---- örnek API yanıtları ----

    public static object Doctor(string id = DoctorId, decimal? minFee = 500m) => new
    {
        id,
        firstName = "Ali",
        lastName = "Veli",
        title = "Dr.",
        gender = "Male",
        degrees = "Kardiyoloji Uzmanı",
        branchId = "b0",
        biography = "Kalp hastalıkları uzmanı.",
        phone = "0555 000 00 00",
        email = "ali@hastane.test",
        services = new[] { "EKG" },
        educations = new[] { new { school = "Ankara Üniversitesi", department = "Tıp", startYear = 2000, endYear = 2006 } },
        experiences = new[] { new { place = "Şehir Hastanesi", position = "Uzman", startYear = 2010, endYear = (int?)null } },
        awards = new[] { new { title = "Yılın Doktoru", year = 2020, description = (string?)null } },
        clinicLocations = new[]
        {
            new
            {
                clinicName = "Merkez Klinik", city = "Ankara", district = "Çankaya", address = "Atatürk Bulvarı 1", floor = "3",
                hourlyFee = minFee, currency = "TRY",
                schedule = new[] { new { dayOfWeek = "Monday", startTime = "09:00", endTime = "12:00", slotMinutes = 30 } },
            },
        },
        primaryCity = "Ankara",
        minFee,
        maxFee = minFee,
        currency = "TRY",
    };

    public static object DoctorSummary(string id = DoctorId) => new { id, firstName = "Ali", lastName = "Veli", title = "Dr.", degrees = "Uzman", branchId = "b0" };

    public static object SearchItem(string id, string first, string last) => new
    {
        id,
        firstName = first,
        lastName = last,
        title = "Dr.",
        degrees = "Uzman",
        gender = "Female",
        branchId = "b0",
        services = new[] { "EKG" },
        primaryCity = "Ankara",
        minFee = 400m,
        maxFee = 600m,
        currency = "TRY",
    };

    public static object Paged(object[] items, long total, int page = 1, int pageSize = 12) => new { items, totalCount = total, page, pageSize };

    public static object Appointment(Guid id, string status = "Pending", string doctorId = DoctorId, DateTime? date = null) => new
    {
        id,
        doctorId,
        patientId = "user-1",
        branchId = "b0",
        appointmentDate = date ?? DateTime.UtcNow.AddDays(3),
        status,
        createdAt = DateTime.UtcNow.AddDays(-1),
    };

    public static object AppointmentWithDetail(Guid id, string status = "Pending") => new
    {
        id,
        doctorId = DoctorId,
        patientId = "user-1",
        branchId = "b0",
        appointmentDate = DateTime.UtcNow.AddDays(3),
        status,
        createdAt = DateTime.UtcNow.AddDays(-1),
        detail = new { complaint = "Göğüs ağrısı", diagnosis = "Gözlem", doctorNotes = "Kontrol", updatedAt = (DateTime?)null },
    };

    public static object Payment(Guid id, string status = "Paid", Guid? appointmentId = null) => new
    {
        id,
        appointmentId = appointmentId ?? Appt1,
        doctorId = DoctorId,
        status,
        amount = 500m,
        currency = "TRY",
        cardBrand = "Visa",
        cardLast4 = "1111",
        failureReason = (string?)null,
        invoiceId = Inv1,
        invoiceNumber = "INV-0001",
        createdAt = DateTime.UtcNow,
        paidAt = DateTime.UtcNow,
    };

    public static object EmptyPayments() => new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 10 };

    public static object Review(string id, string reviewer, int rating = 4, string comment = "Çok memnun kaldım") => new
    {
        id,
        doctorId = DoctorId,
        reviewerName = reviewer,
        rating,
        comment,
        createdAt = DateTime.UtcNow.AddDays(-2),
    };

    public static object Problem(int status, string detail, object? errors = null) => errors is null
        ? new { status, title = "Hata", detail }
        : new { status, title = "Hata", detail, errors };

    public static string LocalFuture(int days = 5)
    {
        var local = DateTime.Now.AddDays(days);
        return new DateTime(local.Year, local.Month, local.Day, 10, 30, 0, DateTimeKind.Local).ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }
}
