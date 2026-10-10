using System.Net;
using System.Text.Json;
using Hastane.TestSupport;

namespace Hastane.WebUI.Tests;

public class DoctorPanelTests
{
    private const string DoctorId = "doc-1";
    private const string PatientGuid = "11111111-1111-1111-1111-111111111111";
    private static readonly Guid ApptId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RxId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static string? Loc(HttpResponseMessage r) => TestUrls.Relative(r.Headers.Location);

    private static async Task<HttpClient> DoctorClientAsync(WebUiTestHost host, string? doctorId = DoctorId)
    {
        var client = host.CreateClient();
        await host.LoginAsync(client, TestJwt.Create("user-1", ["Doctor"], doctorId: doctorId), roles: ["Doctor"]);
        return client;
    }

    private static object Appointment(string status = "Confirmed", DateTime? date = null, object? detail = null) => new
    {
        id = ApptId,
        doctorId = DoctorId,
        patientId = PatientGuid,
        branchId = "b1",
        appointmentDate = date ?? DateTime.UtcNow.AddHours(1),
        status,
        createdAt = DateTime.UtcNow.AddDays(-1),
        detail,
    };

    private static void MapSupport(WebUiTestHost host)
    {
        host.Gateway.MapJson("GET", "/gateway/users", 200, new[] { new { id = PatientGuid, firstName = "Zeynep", lastName = "Kaya" } });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { new { id = "b1", name = "Kardiyoloji" } });
    }

    private static IEnumerable<RecordedRequest> Calls(WebUiTestHost host, string method, string path)
        => host.Gateway.Requests.Where(r => r.Method == method && r.Path == path);

    // Sayfa GET'ten (ör. /Doctor) layout'taki çıkış formunun antiforgery belirtecini alır, hedefe POST eder.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields, bool withToken = true)
    {
        if (withToken)
        {
            var html = await (await client.GetAsync("/Error/404")).Content.ReadAsStringAsync();
            fields["__RequestVerificationToken"] = WebUiTestHost.ExtractAntiforgeryToken(html);
        }

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static void MapReviewsEmpty(WebUiTestHost host)
    {
        host.Gateway.MapJson("GET", "/gateway/reviews", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", $"/gateway/reviews/doctors/{DoctorId}/summary", 200,
            new { doctorId = DoctorId, reviewCount = 0, averageRating = 0, recommendationPercent = 0 });
    }

    // ---------- Yetki ----------

    [Theory]
    [InlineData("/Doctor")]
    [InlineData("/Doctor/Appointments")]
    [InlineData("/Doctor/Prescriptions")]
    [InlineData("/Doctor/Profile")]
    [InlineData("/Doctor/Reviews")]
    public async Task Anonymous_is_redirected_to_login(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login?returnUrl=", Loc(response));
    }

    [Theory]
    [InlineData("Patient", "/Doctor/Appointments")]
    [InlineData("Patient", "/Doctor/Prescriptions")]
    [InlineData("Patient", "/Doctor/Profile")]
    [InlineData("Admin", "/Doctor/Appointments")]
    [InlineData("Admin", "/Doctor/Reviews")]
    public async Task Patient_and_admin_get_403_and_no_api_call(string role, string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = host.CreateClient();
        await host.LoginAsync(client, TestJwt.Create("user-1", [role], doctorId: DoctorId), roles: [role]);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Yetkiniz yok", await response.Content.ReadHtmlAsync());
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/appointments") || r.Path.StartsWith("/gateway/doctors"));
    }

    [Fact]
    public async Task Doctor_without_doctor_id_claim_sees_notice_and_no_api_is_called()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await DoctorClientAsync(host, doctorId: null);
        // Menü zili / mesaj rozeti (kabuk) çağrıları hariç: alan servislerine hiç gidilmez (ApiCallCount, SocialFeaturesTests.cs).
        var before = host.Gateway.ApiCallCount();

        foreach (var path in new[] { "/Doctor", "/Doctor/Appointments", "/Doctor/Prescriptions", "/Doctor/Profile", "/Doctor/Reviews" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("henüz bir doktor kaydına bağlanmadı", await response.Content.ReadHtmlAsync());
        }

        // POST da API'ye gitmez.
        var post = await PostAsync(client, "/Doctor/Prescriptions/Delete/" + RxId, []);
        Assert.Contains("henüz bir doktor kaydına bağlanmadı", await post.Content.ReadHtmlAsync());
        Assert.Equal(before, host.Gateway.ApiCallCount());
    }

    // ---------- Ana sayfa ----------

    [Fact]
    public async Task Home_shows_today_and_pending_with_patient_name_from_single_users_call()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, new[] { Appointment("Pending") });
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor")).Content.ReadHtmlAsync();

        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains("Kardiyoloji", html);
        Assert.Contains("Onay bekliyor", html);
        Assert.Single(Calls(host, "GET", "/gateway/users")); // N+1 yok: iki randevu çağrısı, tek toplu kullanıcı çağrısı
        Assert.Equal(2, Calls(host, "GET", "/gateway/appointments").Count());
        Assert.All(Calls(host, "GET", "/gateway/appointments"), r => Assert.StartsWith("Bearer ", r.Authorization));
    }

    [Fact]
    public async Task Home_shows_empty_states()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor")).Content.ReadHtmlAsync();

        Assert.Contains("Bugün için randevunuz yok", html);
        Assert.Contains("Onay bekleyen randevu yok", html);
        Assert.Contains("Yaklaşan randevu yok", html);
    }

    [Fact]
    public async Task Home_falls_back_to_short_id_when_users_endpoint_is_forbidden()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/branches", 200, Array.Empty<object>());
        host.Gateway.MapStatus("GET", "/gateway/users", 403);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, new[] { Appointment() });
        var client = await DoctorClientAsync(host);

        var response = await client.GetAsync("/Doctor");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hasta #11111111", html);
    }

    // ---------- Randevular ----------

    [Fact]
    public async Task Appointments_list_sends_doctor_id_from_token_and_filters()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        var queries = new List<string>();
        host.Gateway.Map("GET", "/gateway/appointments", ctx =>
        {
            queries.Add(ctx.Request.QueryString.Value ?? "");
            return ctx.Response.WriteAsJsonAsync(new[] { Appointment("Completed") });
        });
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Appointments?Status=Completed&From=2026-10-01&To=2026-10-31&doctorId=baska-doktor")).Content.ReadHtmlAsync();

        var query = Assert.Single(queries);
        Assert.Contains($"doctorId={DoctorId}", query);
        Assert.DoesNotContain("baska-doktor", query);
        Assert.Contains("status=Completed", query);
        Assert.Contains("from=", query);
        Assert.Contains("to=", query);
        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains("Tamamlandı", html);
    }

    [Fact]
    public async Task Appointments_list_shows_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Appointments")).Content.ReadHtmlAsync();

        Assert.Contains("Bu süzgece uyan randevu yok", html);
    }

    [Theory]
    [InlineData("Pending", "Onayla", "İptal et", false)]
    [InlineData("Confirmed", "Tamamla", "İptal et", true)]
    [InlineData("Completed", null, null, true)]
    [InlineData("Cancelled", null, null, false)]
    public async Task Details_offers_only_backend_allowed_transitions(string status, string? primary, string? cancel, bool canWriteRx)
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment(status));
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync($"/Doctor/Appointments/Details/{ApptId}")).Content.ReadHtmlAsync();

        foreach (var label in new[] { "Onayla", "Tamamla", "İptal et" })
        {
            var expected = label == primary || label == cancel;
            Assert.Equal(expected, html.Contains($">{label}</button>"));
        }

        Assert.Equal(canWriteRx, html.Contains("Reçete yaz"));
        Assert.Contains("Zeynep Kaya", html);
    }

    [Fact]
    public async Task Details_shows_saved_examination_detail()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200,
            Appointment(detail: new { id = Guid.NewGuid(), appointmentId = ApptId, complaint = "Göğüs ağrısı", diagnosis = "Gerginlik", doctorNotes = "Kontrol" }));
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync($"/Doctor/Appointments/Details/{ApptId}")).Content.ReadHtmlAsync();

        Assert.Contains("Göğüs ağrısı", html);
        Assert.Contains("Gerginlik", html);
    }

    [Fact]
    public async Task SetStatus_patches_gateway_and_redirects()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("PATCH", $"/gateway/appointments/{ApptId}/status", 200, Appointment("Confirmed"));
        var client = await DoctorClientAsync(host);

        var response = await PostAsync(client, $"/Doctor/Appointments/SetStatus/{ApptId}", new() { ["status"] = "Confirmed" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Doctor/Appointments/Details/{ApptId}", Loc(response));
        var sent = Assert.Single(Calls(host, "PATCH", $"/gateway/appointments/{ApptId}/status"));
        Assert.Equal("Confirmed", JsonDocument.Parse(sent.Body).RootElement.GetProperty("status").GetString());
        Assert.StartsWith("Bearer ", sent.Authorization);
    }

    [Fact]
    public async Task SetStatus_conflict_is_shown_as_warning()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("PATCH", $"/gateway/appointments/{ApptId}/status", 409, new { status = 409, title = "Çakışma", detail = "Pending durumundan Completed durumuna geçilemez." });
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment("Pending"));
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var post = await PostAsync(client, $"/Doctor/Appointments/SetStatus/{ApptId}", new() { ["status"] = "Completed" });
        var html = await (await client.GetAsync(Loc(post)!)).Content.ReadHtmlAsync();

        Assert.Contains("geçilemez", html);
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected_and_no_api_call_is_made()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await DoctorClientAsync(host);

        var response = await PostAsync(client, $"/Doctor/Appointments/SetStatus/{ApptId}", new() { ["status"] = "Confirmed" }, withToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Calls(host, "PATCH", $"/gateway/appointments/{ApptId}/status"));
    }

    [Fact]
    public async Task SaveDetail_puts_to_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("PUT", $"/gateway/appointments/{ApptId}/detail", 201, new { id = Guid.NewGuid(), appointmentId = ApptId, complaint = "x" });
        var client = await DoctorClientAsync(host);

        var response = await PostAsync(client, $"/Doctor/Appointments/SaveDetail/{ApptId}",
            new() { ["Complaint"] = "Baş ağrısı", ["Diagnosis"] = "Migren", ["DoctorNotes"] = "Dinlenme" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var body = JsonDocument.Parse(Assert.Single(Calls(host, "PUT", $"/gateway/appointments/{ApptId}/detail")).Body).RootElement;
        Assert.Equal("Baş ağrısı", body.GetProperty("complaint").GetString());
        Assert.Equal("Migren", body.GetProperty("diagnosis").GetString());
        Assert.Equal("Dinlenme", body.GetProperty("doctorNotes").GetString());
    }

    [Fact]
    public async Task SaveDetail_shows_api_validation_errors_on_the_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("PUT", $"/gateway/appointments/{ApptId}/detail", 400,
            new { status = 400, title = "Doğrulama", errors = new Dictionary<string, string[]> { ["Complaint"] = ["Şikayet çok uzun (sunucu)."] } });
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment());
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var response = await PostAsync(client, $"/Doctor/Appointments/SaveDetail/{ApptId}", new() { ["Complaint"] = "x" });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Şikayet çok uzun (sunucu).", html);
    }

    [Fact]
    public async Task SaveDetail_requires_complaint_without_calling_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment());
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var response = await PostAsync(client, $"/Doctor/Appointments/SaveDetail/{ApptId}", new() { ["Complaint"] = "" });

        Assert.Contains("Şikayet gerekli", await response.Content.ReadHtmlAsync());
        Assert.Empty(Calls(host, "PUT", $"/gateway/appointments/{ApptId}/detail"));
    }

    [Theory]
    [InlineData(404, "Sayfa bulunamadı")]
    [InlineData(403, "Yetkiniz yok")]
    [InlineData(503, "Servis şu an yanıt vermiyor")]
    public async Task Details_maps_api_failures_to_pages(int apiStatus, string expectedText)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", $"/gateway/appointments/{ApptId}", apiStatus);
        var client = await DoctorClientAsync(host);

        var response = await client.GetAsync($"/Doctor/Appointments/Details/{ApptId}");

        Assert.Equal((HttpStatusCode)apiStatus, response.StatusCode);
        Assert.Contains(expectedText, await response.Content.ReadHtmlAsync());
    }

    // ---------- Reçeteler ----------

    [Fact]
    public async Task Prescriptions_list_shows_rows_with_patient_name()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200,
            new[] { new { id = RxId, appointmentId = ApptId, doctorId = DoctorId, patientId = PatientGuid, issuedAt = DateTime.UtcNow, itemCount = 3 } });
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Prescriptions")).Content.ReadHtmlAsync();

        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains($"/Doctor/Prescriptions/Details/{RxId}", html);
        Assert.Single(Calls(host, "GET", "/gateway/users"));
    }

    [Fact]
    public async Task Prescriptions_list_shows_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Prescriptions")).Content.ReadHtmlAsync();

        Assert.Contains("Henüz reçete yazmadınız", html);
    }

    private static object Prescription() => new
    {
        id = RxId,
        appointmentId = ApptId,
        doctorId = DoctorId,
        patientId = PatientGuid,
        issuedAt = DateTime.UtcNow,
        notes = "Bol su için",
        items = new[]
        {
            new { id = Guid.NewGuid(), lineNo = 1, medicineName = "Parol", dosage = "500 mg", frequency = "Günde 2", durationDays = 5, instructions = (string?)"Tok karnına" },
            new { id = Guid.NewGuid(), lineNo = 2, medicineName = "Aferin", dosage = "1 tablet", frequency = "Günde 1", durationDays = 3, instructions = (string?)null },
        },
    };

    [Fact]
    public async Task Prescription_details_lists_items()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/prescriptions/{RxId}", 200, Prescription());
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync($"/Doctor/Prescriptions/Details/{RxId}")).Content.ReadHtmlAsync();

        Assert.Contains("Parol", html);
        Assert.Contains("Aferin", html);
        Assert.Contains("Bol su için", html);
        Assert.Contains("Zeynep Kaya", html);
    }

    [Fact]
    public async Task Create_form_opens_for_confirmed_appointment_and_redirects_for_pending()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment("Confirmed"));
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync($"/Doctor/Prescriptions/Create?appointmentId={ApptId}")).Content.ReadHtmlAsync();
        Assert.Contains("Reçete yaz", html);
        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains("Kalem 1", html);

        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment("Pending"));
        var pending = await client.GetAsync($"/Doctor/Prescriptions/Create?appointmentId={ApptId}");
        Assert.Equal(HttpStatusCode.Redirect, pending.StatusCode);
        Assert.Equal($"/Doctor/Appointments/Details/{ApptId}", Loc(pending));
    }

    private static Dictionary<string, string> ItemFields(int index, string name = "Parol") => new()
    {
        [$"Items[{index}].MedicineName"] = name,
        [$"Items[{index}].Dosage"] = "500 mg",
        [$"Items[{index}].Frequency"] = "Günde 2",
        [$"Items[{index}].DurationDays"] = "5",
        [$"Items[{index}].Instructions"] = "Tok karnına",
    };

    [Fact]
    public async Task Create_posts_items_with_patient_from_appointment_and_no_doctor_id()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment("Confirmed"));
        host.Gateway.MapJson("POST", "/gateway/prescriptions", 201, Prescription());
        var client = await DoctorClientAsync(host);
        var fields = new Dictionary<string, string>(ItemFields(0))
        {
            ["AppointmentId"] = ApptId.ToString(),
            ["Notes"] = "Not",
            ["PatientId"] = "kurban-hasta", // formdan gelen kimlik yok sayılmalı
            ["DoctorId"] = "baska-doktor",
        };
        foreach (var kv in ItemFields(1, "Aferin")) fields[kv.Key] = kv.Value;

        var response = await PostAsync(client, "/Doctor/Prescriptions/Create", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Doctor/Prescriptions/Details/{RxId}", Loc(response));
        var body = JsonDocument.Parse(Assert.Single(Calls(host, "POST", "/gateway/prescriptions")).Body).RootElement;
        Assert.Equal(ApptId.ToString(), body.GetProperty("appointmentId").GetString());
        Assert.Equal(PatientGuid, body.GetProperty("patientId").GetString());
        Assert.False(body.TryGetProperty("doctorId", out _));
        Assert.Equal("Not", body.GetProperty("notes").GetString());
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("Parol", items[0].GetProperty("medicineName").GetString());
        Assert.Equal(5, items[0].GetProperty("durationDays").GetInt32());
        Assert.Equal("Aferin", items[1].GetProperty("medicineName").GetString());
    }

    [Fact]
    public async Task Create_maps_nested_api_errors_onto_item_rows()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment("Confirmed"));
        host.Gateway.MapJson("POST", "/gateway/prescriptions", 400, new
        {
            status = 400,
            title = "Doğrulama",
            errors = new Dictionary<string, string[]> { ["Items[1].Dosage"] = ["Doz geçersiz (sunucu)."], ["Notes"] = ["Not geçersiz (sunucu)."] },
        });
        var client = await DoctorClientAsync(host);
        var fields = new Dictionary<string, string>(ItemFields(0)) { ["AppointmentId"] = ApptId.ToString() };
        foreach (var kv in ItemFields(1, "Aferin")) fields[kv.Key] = kv.Value;

        var response = await PostAsync(client, "/Doctor/Prescriptions/Create", fields);
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Doz geçersiz (sunucu).", html);
        Assert.Contains("Not geçersiz (sunucu).", html);
        // Hata ikinci kalemin alanının yanında (Items_1__Dosage), girilen değerler korunmuş.
        Assert.Contains("data-valmsg-for=\"Items[1].Dosage\"", await response.Content.ReadAsStringAsync());
        Assert.Contains("Aferin", html);
    }

    [Fact]
    public async Task Create_validates_required_item_fields_without_calling_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment("Confirmed"));
        var client = await DoctorClientAsync(host);
        var fields = new Dictionary<string, string>(ItemFields(0, "")) { ["AppointmentId"] = ApptId.ToString() };

        var html = await (await PostAsync(client, "/Doctor/Prescriptions/Create", fields)).Content.ReadHtmlAsync();

        Assert.Contains("İlaç adı gerekli", html);
        Assert.Empty(Calls(host, "POST", "/gateway/prescriptions"));
    }

    [Fact]
    public async Task Create_add_and_remove_item_buttons_redraw_form_without_saving()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment("Confirmed"));
        var client = await DoctorClientAsync(host);
        var fields = new Dictionary<string, string>(ItemFields(0, "Birinci")) { ["AppointmentId"] = ApptId.ToString(), ["op"] = "add-item" };

        var added = await (await PostAsync(client, "/Doctor/Prescriptions/Create", fields)).Content.ReadHtmlAsync();
        Assert.Contains("Kalem 2", added);
        Assert.Contains("Birinci", added);

        var two = new Dictionary<string, string>(ItemFields(0, "Birinci")) { ["AppointmentId"] = ApptId.ToString(), ["op"] = "remove-item:0" };
        foreach (var kv in ItemFields(1, "Ikinci")) two[kv.Key] = kv.Value;
        var removed = await (await PostAsync(client, "/Doctor/Prescriptions/Create", two)).Content.ReadHtmlAsync();
        Assert.DoesNotContain("Birinci", removed);
        Assert.Contains("Ikinci", removed);
        Assert.DoesNotContain("Kalem 2", removed);

        Assert.Empty(Calls(host, "POST", "/gateway/prescriptions"));
    }

    [Fact]
    public async Task Edit_form_is_prefilled_and_put_uses_route_id_with_replacement_items()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/prescriptions/{RxId}", 200, Prescription());
        host.Gateway.MapStatus("PUT", $"/gateway/prescriptions/{RxId}", 200);
        var client = await DoctorClientAsync(host);

        var form = await (await client.GetAsync($"/Doctor/Prescriptions/Edit/{RxId}")).Content.ReadHtmlAsync();
        Assert.Contains("Parol", form);
        Assert.Contains("Tok karnına", form);

        var fields = new Dictionary<string, string>(ItemFields(0, "Yeni ilaç")) { ["Notes"] = "Yeni not" };
        var response = await PostAsync(client, $"/Doctor/Prescriptions/Edit/{RxId}", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var body = JsonDocument.Parse(Assert.Single(Calls(host, "PUT", $"/gateway/prescriptions/{RxId}")).Body).RootElement;
        Assert.Equal(RxId.ToString(), body.GetProperty("id").GetString());
        Assert.Equal("Yeni not", body.GetProperty("notes").GetString());
        Assert.Equal("Yeni ilaç", body.GetProperty("items")[0].GetProperty("medicineName").GetString());
        Assert.False(body.TryGetProperty("doctorId", out _));
    }

    [Fact]
    public async Task Delete_is_a_post_and_calls_gateway_delete()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("DELETE", $"/gateway/prescriptions/{RxId}", 204);
        var client = await DoctorClientAsync(host);

        // GET ile silinemez.
        Assert.NotEqual(HttpStatusCode.Redirect, (await client.GetAsync($"/Doctor/Prescriptions/Delete/{RxId}")).StatusCode);
        Assert.Empty(Calls(host, "DELETE", $"/gateway/prescriptions/{RxId}"));

        var response = await PostAsync(client, $"/Doctor/Prescriptions/Delete/{RxId}", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Doctor/Prescriptions", Loc(response));
        Assert.Single(Calls(host, "DELETE", $"/gateway/prescriptions/{RxId}"));
    }

    [Fact]
    public async Task Prescription_of_another_doctor_gives_404_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", $"/gateway/prescriptions/{RxId}", 404);
        var client = await DoctorClientAsync(host);

        var response = await client.GetAsync($"/Doctor/Prescriptions/Details/{RxId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Profil ----------

    private static object Doctor() => new
    {
        id = DoctorId,
        firstName = "Ahmet",
        lastName = "Demir",
        title = "Dr.",
        gender = "Male",
        degrees = "Uzman",
        branchId = "b1",
        biography = "Özgeçmiş metni",
        imageUrl = (string?)null,
        phone = "555",
        email = "ahmet@test.local",
        services = new[] { "EKG", "Efor testi" },
        gallery = Array.Empty<string>(),
        socialLinks = new { facebook = (string?)null },
        educations = new[] { new { school = "İstanbul Üniversitesi", department = "Tıp", startYear = 2000, endYear = 2006 } },
        experiences = new[] { new { place = "Şehir Hastanesi", position = "Uzman", startYear = 2010, endYear = (int?)null } },
        awards = new[] { new { title = "Yılın Doktoru", year = 2020, description = (string?)null } },
        clinicLocations = new[]
        {
            new
            {
                clinicName = "Merkez Klinik", city = "Ankara", district = "Çankaya", address = (string?)null, floor = (string?)null,
                hourlyFee = (decimal?)1500m, currency = "TRY",
                schedule = new[] { new { dayOfWeek = "Monday", startTime = "09:00", endTime = "12:00", slotMinutes = 30 } },
            },
        },
    };

    private static void MapProfile(WebUiTestHost host)
    {
        host.Gateway.MapJson("GET", $"/gateway/doctors/{DoctorId}", 200, Doctor());
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { new { id = "b1", name = "Kardiyoloji" }, new { id = "b2", name = "Dahiliye" } });
    }

    [Fact]
    public async Task Profile_shows_all_nested_lists()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapProfile(host);
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Profile")).Content.ReadHtmlAsync();

        foreach (var expected in new[] { "Ahmet", "Demir", "EKG", "Efor testi", "İstanbul Üniversitesi", "Şehir Hastanesi", "Yılın Doktoru", "Merkez Klinik", "09:00", "1500", "Kardiyoloji", "Dahiliye" })
            Assert.Contains(expected, html);
        Assert.Contains($"/gateway/doctors/{DoctorId}", host.Gateway.Requests.Select(r => r.Path));
    }

    private static Dictionary<string, string> ProfileFields() => new()
    {
        ["FirstName"] = "Ahmet",
        ["LastName"] = "Demir",
        ["Title"] = "Prof.",
        ["Gender"] = "Male",
        ["BranchId"] = "b1",
        ["Services[0]"] = "EKG",
        ["Services[1]"] = "",
        ["Educations[0].School"] = "Ankara Üniversitesi",
        ["Educations[0].Department"] = "Tıp",
        ["Educations[0].StartYear"] = "2000",
        ["Educations[0].EndYear"] = "2006",
        ["ClinicLocations[0].ClinicName"] = "Merkez Klinik",
        ["ClinicLocations[0].City"] = "Ankara",
        ["ClinicLocations[0].HourlyFee"] = "1500",
        ["ClinicLocations[0].Currency"] = "TRY",
        ["ClinicLocations[0].Schedule[0].DayOfWeek"] = "Tuesday",
        ["ClinicLocations[0].Schedule[0].StartTime"] = "10:00",
        ["ClinicLocations[0].Schedule[0].EndTime"] = "13:00",
        ["ClinicLocations[0].Schedule[0].SlotMinutes"] = "20",
    };

    [Fact]
    public async Task Profile_update_always_uses_doctor_id_from_token_not_from_request()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapProfile(host);
        host.Gateway.MapStatus("PUT", $"/gateway/doctors/{DoctorId}", 204);
        host.Gateway.MapStatus("PUT", "/gateway/doctors/baska-doktor", 204);
        var client = await DoctorClientAsync(host);
        var fields = ProfileFields();
        fields["Id"] = "baska-doktor";
        fields["id"] = "baska-doktor";
        fields["DoctorId"] = "baska-doktor";

        var response = await PostAsync(client, "/Doctor/Profile?id=baska-doktor&doctorId=baska-doktor", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.Contains("baska-doktor"));
        var body = JsonDocument.Parse(Assert.Single(Calls(host, "PUT", $"/gateway/doctors/{DoctorId}")).Body).RootElement;
        Assert.Equal(DoctorId, body.GetProperty("id").GetString());
        Assert.Equal("Prof.", body.GetProperty("title").GetString());
        Assert.Equal("Male", body.GetProperty("gender").GetString());
        Assert.Equal(["EKG"], body.GetProperty("services").EnumerateArray().Select(e => e.GetString()).ToArray()); // boş satır atılır
        Assert.Equal("Ankara Üniversitesi", body.GetProperty("educations")[0].GetProperty("school").GetString());
        var clinic = body.GetProperty("clinicLocations")[0];
        Assert.Equal(1500m, clinic.GetProperty("hourlyFee").GetDecimal());
        Assert.Equal("Tuesday", clinic.GetProperty("schedule")[0].GetProperty("dayOfWeek").GetString());
        Assert.Equal(20, clinic.GetProperty("schedule")[0].GetProperty("slotMinutes").GetInt32());
    }

    [Fact]
    public async Task Profile_maps_nested_api_errors_onto_fields()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapProfile(host);
        host.Gateway.MapJson("PUT", $"/gateway/doctors/{DoctorId}", 400, new
        {
            status = 400,
            title = "Doğrulama",
            errors = new Dictionary<string, string[]>
            {
                ["Educations[0].School"] = ["Okul adı geçersiz (sunucu)."],
                ["ClinicLocations[0].Schedule"] = ["Saat aralıkları çakışıyor (sunucu)."],
            },
        });
        var client = await DoctorClientAsync(host);

        var response = await PostAsync(client, "/Doctor/Profile", ProfileFields());
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Okul adı geçersiz (sunucu).", html);
        Assert.Contains("Saat aralıkları çakışıyor (sunucu).", html);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-valmsg-for=\"Educations[0].School\"", raw);
        Assert.Contains("Ankara Üniversitesi", html); // girilen değerler korunur
    }

    [Fact]
    public async Task Profile_add_and_remove_buttons_redraw_nested_lists_without_saving()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapProfile(host);
        var client = await DoctorClientAsync(host);

        var addEdu = ProfileFields();
        addEdu["op"] = "add:Educations";
        var html = await (await PostAsync(client, "/Doctor/Profile", addEdu)).Content.ReadAsStringAsync();
        Assert.Contains("name=\"Educations[1].School\"", html);

        var addSlot = ProfileFields();
        addSlot["op"] = "add-slot:0";
        html = await (await PostAsync(client, "/Doctor/Profile", addSlot)).Content.ReadAsStringAsync();
        Assert.Contains("name=\"ClinicLocations[0].Schedule[1].StartTime\"", html);

        var removeEdu = ProfileFields();
        removeEdu["op"] = "remove:Educations:0";
        html = await (await PostAsync(client, "/Doctor/Profile", removeEdu)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("name=\"Educations[0].School\"", html);

        var addClinic = ProfileFields();
        addClinic["op"] = "add:ClinicLocations";
        html = await (await PostAsync(client, "/Doctor/Profile", addClinic)).Content.ReadAsStringAsync();
        Assert.Contains("name=\"ClinicLocations[1].ClinicName\"", html);

        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "PUT");
    }

    // ---------- Yorumlar ----------

    [Fact]
    public async Task Reviews_show_summary_and_comments_encoded()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var queries = new List<string>();
        host.Gateway.Map("GET", "/gateway/reviews", ctx =>
        {
            queries.Add(ctx.Request.QueryString.Value ?? "");
            return ctx.Response.WriteAsJsonAsync(new[]
            {
                new { id = "r1", doctorId = DoctorId, reviewerName = "Mehmet", rating = 5, comment = "<script>alert(1)</script> harika", createdAt = DateTime.UtcNow },
            });
        });
        host.Gateway.MapJson("GET", $"/gateway/reviews/doctors/{DoctorId}/summary", 200,
            new { doctorId = DoctorId, reviewCount = 12, averageRating = 4.6, recommendationPercent = 91 });
        var client = await DoctorClientAsync(host);

        var response = await client.GetAsync("/Doctor/Reviews");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal($"?doctorId={DoctorId}", Assert.Single(queries));
        Assert.Contains("Mehmet", raw);
        Assert.Contains("harika", raw);
        Assert.DoesNotContain("<script>alert(1)</script>", raw);
        Assert.Contains("%91", raw);
        Assert.Contains("4.6", raw.Replace(',', '.'));
    }

    [Fact]
    public async Task Reviews_show_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapReviewsEmpty(host);
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Reviews")).Content.ReadHtmlAsync();

        Assert.Contains("Henüz yorum yok", html);
    }
}
