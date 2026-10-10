using System.Text.Json;

namespace Hastane.WebUI.Tests;

// Yönetim paneli testlerinin ortak yardımcıları. WebUiTestHost'a dokunmadan, onun üstüne kurulur.
internal static class AdminPanelTestHelpers
{
    public const string BranchId = "br1";
    public const string DoctorId = "doc1";
    public static readonly Guid AppointmentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid PaymentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid InvoiceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid PrescriptionId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static async Task<HttpClient> AdminClientAsync(WebUiTestHost host)
    {
        var client = host.CreateClient();
        await host.LoginAsync(client, WebUiTestHost.NewToken("Admin"), roles: ["Admin"]);
        return client;
    }

    public static async Task<HttpClient> ClientAsync(WebUiTestHost host, string role)
    {
        var client = host.CreateClient();
        await host.LoginAsync(client, WebUiTestHost.NewToken(role), roles: [role]);
        return client;
    }

    /// <summary>Antiforgery belirtecini <paramref name="tokenPage"/> sayfasından alıp <paramref name="postPath"/>'e POST eder
    /// (yalnız POST kabul eden eylemler için: o yolu GET etmek anlamsız).</summary>
    public static async Task<HttpResponseMessage> PostAsync(WebUiTestHost host, HttpClient client, string tokenPage, string postPath, Dictionary<string, string>? fields = null)
    {
        var html = await (await client.GetAsync(tokenPage)).Content.ReadAsStringAsync();
        var form = new Dictionary<string, string>(fields ?? [])
        {
            ["__RequestVerificationToken"] = WebUiTestHost.ExtractAntiforgeryToken(html),
        };
        return await client.PostAsync(postPath, new FormUrlEncodedContent(form));
    }

    public static string? Location(HttpResponseMessage response) => TestUrls.Relative(response.Headers.Location);

    public static RecordedRequest Last(WebUiTestHost host, string method, string path)
        => host.Gateway.Requests.Last(r => r.Method == method && r.Path == path);

    public static bool Called(WebUiTestHost host, string method, string path)
        => host.Gateway.Requests.Any(r => r.Method == method && r.Path == path);

    public static JsonElement Json(RecordedRequest request) => JsonDocument.Parse(request.Body).RootElement;

    /// <summary>Gateway yolunu verilen sorgu dizesi kaydıyla eşler (FakeGateway yalnız yolu kaydeder).</summary>
    public static Func<string?> MapCapturingQuery(WebUiTestHost host, string method, string path, int status, object body)
    {
        string? query = null;
        host.Gateway.Map(method, path, ctx =>
        {
            query = ctx.Request.QueryString.Value;
            ctx.Response.StatusCode = status;
            return ctx.Response.WriteAsJsonAsync(body);
        });
        return () => query;
    }

    public static async Task<string> HtmlAsync(HttpResponseMessage response) => await response.Content.ReadHtmlAsync();

    public static object Branch(string id = BranchId, string name = "Kardiyoloji")
        => new { id, name, description = name + " açıklaması", icon = "heart" };

    public static object DoctorListItem(string id = DoctorId, string first = "Ali", string last = "Veli", string branchId = BranchId)
        => new { id, firstName = first, lastName = last, title = "Dr.", branchId, primaryCity = "Ankara", minFee = 100m, maxFee = 200m, currency = "TRY" };

    public static object DoctorDetail() => new
    {
        id = DoctorId,
        firstName = "Ali",
        lastName = "Veli",
        title = "Dr.",
        gender = "Male",
        degrees = "Uzman",
        branchId = BranchId,
        biography = "Biyografi",
        imageUrl = "https://x.test/a.jpg",
        phone = "0555",
        email = "ali@test.local",
        services = new[] { "Muayene" },
        gallery = new[] { "https://x.test/g1.jpg" },
        socialLinks = new { facebook = "https://fb.test/a" },
        educations = new[] { new { school = "ODTÜ", department = "Tıp", startYear = 2000, endYear = 2006 } },
        experiences = new[] { new { place = "Hastane A", position = "Asistan", startYear = 2006, endYear = (int?)2010 } },
        awards = new[] { new { title = "Ödül 1", year = 2012, description = "Açıklama" } },
        clinicLocations = new[]
        {
            new
            {
                clinicName = "Merkez Klinik",
                city = "Ankara",
                district = "Çankaya",
                address = "Adres 1",
                floor = "3",
                hourlyFee = (decimal?)450.5m,
                currency = "TRY",
                schedule = new[] { new { dayOfWeek = "Monday", startTime = "09:00", endTime = "12:00", slotMinutes = 30 } },
            },
        },
    };

    public static object Appointment(Guid? id = null) => new
    {
        id = id ?? AppointmentId,
        doctorId = DoctorId,
        patientId = "patient-guid-1",
        branchId = BranchId,
        appointmentDate = new DateTime(2026, 10, 20, 9, 30, 0, DateTimeKind.Utc),
        status = "Confirmed",
        createdAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
    };

    public static object Payment(string status = "Paid") => new
    {
        id = PaymentId,
        appointmentId = AppointmentId,
        patientId = "patient-guid-1",
        doctorId = DoctorId,
        status,
        amount = 450.5m,
        currency = "TRY",
        cardBrand = "Visa",
        cardLast4 = "4242",
        invoiceId = InvoiceId,
        invoiceNumber = "INV-0001",
        createdAt = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc),
        paidAt = new DateTime(2026, 10, 2, 8, 1, 0, DateTimeKind.Utc),
    };

    public static object Paged(object[] items, int page = 1, int pageSize = 10, int? total = null)
        => new { items, totalCount = total ?? items.Length, page, pageSize };

    public static object Invoice() => new
    {
        id = InvoiceId,
        invoiceNumber = "INV-0001",
        paymentId = PaymentId,
        patientId = "patient-guid-1",
        doctorId = DoctorId,
        issuedAt = new DateTime(2026, 10, 2, 8, 1, 0, DateTimeKind.Utc),
        currency = "TRY",
        subtotal = 417.13m,
        taxPercent = 8m,
        taxAmount = 33.37m,
        total = 450.5m,
        lines = new[] { new { id = Guid.NewGuid(), description = "Muayene ücreti", quantity = 1, unitPrice = 417.13m, lineTotal = 417.13m } },
    };

    public static object User(string[]? roles = null, string? doctorId = null) => new
    {
        id = "user-9",
        firstName = "Zeynep",
        lastName = "Kaya",
        email = "zeynep@test.local",
        roles = roles ?? ["Patient"],
        doctorId,
    };
}
