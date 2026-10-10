using System.Net;
using System.Text.Json;
using Hastane.TestSupport;
using static Hastane.WebUI.Tests.SlotTestHelpers;

namespace Hastane.WebUI.Tests;

// Doktor paneli: Hastalarım, haftalık takvim, yeniden planlama, reçete yazdırma. Sahte gateway deseni (UI-KURALLARI §7).
public class DoctorNewScreensTests
{
    private const string DoctorId = "doc-1";
    private const string PatientA = "11111111-1111-1111-1111-111111111111";
    private const string PatientB = "22222222-2222-2222-2222-222222222222";
    private static readonly Guid Appt1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Appt2 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Appt3 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid RxId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static string? Loc(HttpResponseMessage r) => TestUrls.Relative(r.Headers.Location);

    private static async Task<HttpClient> DoctorClientAsync(WebUiTestHost host)
    {
        var client = host.CreateClient();
        await host.LoginAsync(client, TestJwt.Create("user-1", ["Doctor"], doctorId: DoctorId), roles: ["Doctor"]);
        return client;
    }

    private static object Appt(Guid id, string patientId, string status = "Confirmed", DateTime? date = null) => new
    {
        id,
        doctorId = DoctorId,
        patientId,
        branchId = "b1",
        appointmentDate = date ?? DateTime.UtcNow.AddDays(1),
        status,
        createdAt = DateTime.UtcNow.AddDays(-1),
    };

    private static void MapSupport(WebUiTestHost host)
    {
        host.Gateway.MapJson("GET", "/gateway/users", 200, new[]
        {
            new { id = PatientA, firstName = "Zeynep", lastName = "Kaya" },
            new { id = PatientB, firstName = "Ali", lastName = "Demir" },
        });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { new { id = "b1", name = "Kardiyoloji" } });
    }

    // Antiforgery belirteci layout'taki çıkış formundan (hata sayfası da _Layout kullanır).
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields, bool withToken = true)
    {
        if (withToken)
        {
            var html = await (await client.GetAsync("/Error/404")).Content.ReadAsStringAsync();
            fields["__RequestVerificationToken"] = WebUiTestHost.ExtractAntiforgeryToken(html);
        }

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    // ---------- Hastalarım ----------

    [Fact]
    public async Task Patients_lists_unique_patients_with_count_last_date_and_one_users_call()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        var last = DateTime.UtcNow.AddDays(3);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, new[]
        {
            Appt(Appt1, PatientA, date: DateTime.UtcNow.AddDays(-5)),
            Appt(Appt2, PatientA, date: last),
            Appt(Appt3, PatientB, date: DateTime.UtcNow.AddDays(-1)),
        });
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Patients")).Content.ReadHtmlAsync();

        Assert.Contains("Hastalarım", html);
        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains("Ali Demir", html);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "<td>[12]</td>").Count); // randevu sayıları: 2 ve 1
        Assert.Contains(last.AddHours(3).ToString("dd.MM.yyyy HH:mm"), html);
        Assert.Contains($"/Doctor/Appointments/Details/{Appt2}", html); // Zeynep'in son randevusu
        Assert.Single(host.Gateway.Calls("GET", "/gateway/users")); // N+1 yok
        var call = host.Gateway.Calls("GET", "/gateway/appointments").Single();
        Assert.StartsWith("Bearer ", call.Authorization);
    }

    [Fact]
    public async Task Patients_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        Assert.Contains("Henüz randevusu olan hasta yok", await (await client.GetAsync("/Doctor/Patients")).Content.ReadHtmlAsync());
    }

    [Theory]
    [InlineData("/Doctor/Patients")]
    [InlineData("/Doctor/Calendar")]
    [InlineData("/Doctor/Appointments/Reschedule/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("/Doctor/Prescriptions/Print/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")]
    public async Task New_doctor_pages_block_anonymous_and_other_roles_without_api_calls(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var anonymous = await host.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login?returnUrl=", Loc(anonymous));

        foreach (var role in new[] { "Patient", "Admin" })
        {
            var client = host.CreateClient();
            await host.LoginAsync(client, TestJwt.Create("user-1", [role], doctorId: DoctorId), roles: [role]);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }

        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/appointments") || r.Path.StartsWith("/gateway/prescriptions"));
    }

    [Fact]
    public async Task New_doctor_pages_show_notice_when_account_is_not_linked_and_call_nothing()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = host.CreateClient();
        await host.LoginAsync(client, TestJwt.Create("user-1", ["Doctor"], doctorId: null), roles: ["Doctor"]);
        var before = host.Gateway.Requests.Count;

        foreach (var path in new[] { "/Doctor/Patients", "/Doctor/Calendar" })
            Assert.Contains("henüz bir doktor kaydına bağlanmadı", await (await client.GetAsync(path)).Content.ReadHtmlAsync());

        Assert.Equal(before, host.Gateway.Requests.Count);
    }

    // ---------- Haftalık takvim ----------

    [Fact]
    public async Task Calendar_renders_seven_columns_with_appointments_and_week_range_query()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        var noon = SlotUtc(0, 12); // bugün 12:00 İstanbul (sunucu saat diliminden bağımsız)
        var query = host.Gateway.MapLogged("GET", "/gateway/appointments", 200, new[] { Appt(Appt1, PatientA, "Pending", noon) });
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Calendar")).Content.ReadHtmlAsync();

        Assert.Equal(7, System.Text.RegularExpressions.Regex.Matches(html, "<section class=\"week-col").Count);
        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains("st-Pending", html);
        Assert.Contains($"/Doctor/Appointments/Details/{Appt1}", html);
        Assert.Contains("12:00", html);
        var q = System.Web.HttpUtility.ParseQueryString(query.LastQuery);
        Assert.Equal(DoctorId, q["doctorId"]);
        Assert.NotNull(q["from"]);
        Assert.NotNull(q["to"]);
        var from = DateTime.Parse(q["from"]!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        var to = DateTime.Parse(q["to"]!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.Equal(DayOfWeek.Monday, from.ToUniversalTime().AddHours(3).DayOfWeek); // hafta İstanbul gününe göre
        Assert.InRange((to - from).TotalDays, 6.9, 7.1); // yedi gün (saat dilimi geçişi için küçük pay)
    }

    [Fact]
    public async Task Calendar_start_parameter_moves_the_week_and_links_previous_and_next()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Calendar?start=2026-10-14")).Content.ReadHtmlAsync();

        Assert.Contains("12.10.2026 - 18.10.2026", html); // 14 Ekim 2026 Çarşamba -> hafta Pazartesi 12'sinde başlar
        Assert.Contains("start=2026-10-05", html);
        Assert.Contains("start=2026-10-19", html);
        Assert.Contains("Bu hafta için randevu yok", html);
    }

    // ---------- Yeniden planlama ----------

    [Fact]
    public async Task Appointment_details_offers_reschedule_only_for_open_appointments()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, Appt(Appt1, PatientA, "Confirmed"));
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt2}", 200, Appt(Appt2, PatientA, "Completed"));
        var client = await DoctorClientAsync(host);

        Assert.Contains($"/Doctor/Appointments/Reschedule/{Appt1}", await (await client.GetAsync($"/Doctor/Appointments/Details/{Appt1}")).Content.ReadHtmlAsync());
        Assert.DoesNotContain("/Reschedule/", await (await client.GetAsync($"/Doctor/Appointments/Details/{Appt2}")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Reschedule_form_shows_patient_and_slots_and_posts_put_with_slot_utc_and_branch_from_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, Appt(Appt1, PatientA, "Pending"));
        host.Gateway.MapJson("PUT", $"/gateway/appointments/{Appt1}", 200, Appt(Appt1, PatientA, "Pending"));
        var availability = host.Gateway.MapAvailability(SlotUtc(3, 15), SlotUtc(3, 15, 30));
        var client = await DoctorClientAsync(host);
        var form = await (await client.GetAsync($"/Doctor/Appointments/Reschedule/{Appt1}?date={DayText(3)}")).Content.ReadHtmlAsync();
        var slot = SlotUtc(3, 15);

        var response = await PostAsync(client, $"/Doctor/Appointments/Reschedule/{Appt1}", new() { ["SlotStart"] = SlotValue(slot), ["Date"] = DayText(3), ["BranchId"] = "kotu" });

        Assert.Contains("Zeynep Kaya", form);
        Assert.DoesNotContain("datetime-local", form);
        Assert.Equal($"?doctorId={DoctorId}&date={DayText(3)}", availability.LastQuery);
        Assert.Contains(">15:00</label>", form);
        Assert.Contains(">15:30</label>", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Doctor/Appointments/Details/{Appt1}", Loc(response));
        var sent = Assert.Single(host.Gateway.Calls("PUT", $"/gateway/appointments/{Appt1}"));
        Assert.StartsWith("Bearer ", sent.Authorization);
        var json = JsonDocument.Parse(sent.Body).RootElement;
        Assert.Equal("b1", json.GetProperty("branchId").GetString());
        Assert.Equal(slot, json.GetProperty("appointmentDate").GetDateTime());
        Assert.False(json.TryGetProperty("patientId", out _));
    }

    [Theory]
    [InlineData(409, "Bu doktorun bu saatte başka bir randevusu var.")]
    [InlineData(400, "Seçilen saat doktorun çalışma programında değil")]
    public async Task Reschedule_api_error_message_is_shown_and_selected_slot_is_kept(int status, string message)
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, Appt(Appt1, PatientA, "Confirmed"));
        host.Gateway.MapJson("PUT", $"/gateway/appointments/{Appt1}", status, new { status, title = "Hata", detail = message });
        host.Gateway.MapAvailability(SlotUtc(3, 15), SlotUtc(3, 16));
        var client = await DoctorClientAsync(host);
        var slot = SlotUtc(3, 15);

        var response = await PostAsync(client, $"/Doctor/Appointments/Reschedule/{Appt1}", new() { ["SlotStart"] = SlotValue(slot), ["Date"] = DayText(3) });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, html);
        Assert.Contains($"value=\"{SlotValue(slot)}\" checked", html);
    }

    [Fact]
    public async Task Reschedule_503_is_a_form_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, Appt(Appt1, PatientA, "Confirmed"));
        host.Gateway.MapStatus("PUT", $"/gateway/appointments/{Appt1}", 503);
        host.Gateway.MapAvailability(SlotUtc(3, 15));
        var client = await DoctorClientAsync(host);

        var response = await PostAsync(client, $"/Doctor/Appointments/Reschedule/{Appt1}", new() { ["SlotStart"] = SlotValue(SlotUtc(3, 15)), ["Date"] = DayText(3) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Doktorun programına şu an ulaşılamıyor", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Reschedule_of_finished_appointment_redirects_and_past_or_missing_slot_never_reaches_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, Appt(Appt1, PatientA, "Confirmed"));
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt2}", 200, Appt(Appt2, PatientA, "Cancelled"));
        host.Gateway.MapAvailability(SlotUtc(3, 15));
        var client = await DoctorClientAsync(host);

        var closed = await client.GetAsync($"/Doctor/Appointments/Reschedule/{Appt2}");
        var pastHtml = await (await PostAsync(client, $"/Doctor/Appointments/Reschedule/{Appt1}", new() { ["SlotStart"] = SlotValue(DateTime.UtcNow.AddDays(-2)) })).Content.ReadHtmlAsync();
        var missingHtml = await (await PostAsync(client, $"/Doctor/Appointments/Reschedule/{Appt1}", new())).Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.Redirect, closed.StatusCode);
        Assert.Equal($"/Doctor/Appointments/Details/{Appt2}", Loc(closed));
        Assert.Contains("Randevu tarihi gelecekte olmalı.", pastHtml);
        Assert.Contains("Bir saat seçin.", missingHtml);
        Assert.Empty(host.Gateway.Calls("PUT", $"/gateway/appointments/{Appt1}"));
    }

    [Fact]
    public async Task Reschedule_post_without_antiforgery_token_is_rejected()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await DoctorClientAsync(host);

        var response = await PostAsync(client, $"/Doctor/Appointments/Reschedule/{Appt1}", new() { ["SlotStart"] = SlotValue(SlotUtc(3, 15)) }, withToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("PUT", $"/gateway/appointments/{Appt1}"));
    }

    // ---------- Reçete yazdırma ----------

    [Fact]
    public async Task Prescription_print_has_no_menu_and_details_links_to_it()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", $"/gateway/prescriptions/{RxId}", 200, new
        {
            id = RxId,
            appointmentId = Appt1,
            doctorId = DoctorId,
            patientId = PatientA,
            issuedAt = DateTime.UtcNow,
            notes = "Bol sıvı",
            items = new[] { new { lineNo = 1, medicineName = "Parol", dosage = "500 mg", frequency = "Günde 3", durationDays = 5, instructions = "Tok karnına" } },
        });
        var client = await DoctorClientAsync(host);

        var print = await client.GetAsync($"/Doctor/Prescriptions/Print/{RxId}");
        var html = await print.Content.ReadHtmlAsync();
        var details = await (await client.GetAsync($"/Doctor/Prescriptions/Details/{RxId}")).Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, print.StatusCode);
        Assert.Contains("window.print()", html);
        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains("Parol", html);
        Assert.Contains("Bol sıvı", html);
        Assert.DoesNotContain("site-nav", html);
        Assert.DoesNotContain("panel-side", html);
        Assert.DoesNotContain("Çıkış yap", html);
        Assert.Contains($"/Doctor/Prescriptions/Print/{RxId}", details);
    }

    [Fact]
    public async Task Prescription_print_of_someone_elses_prescription_is_404()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", $"/gateway/prescriptions/{RxId}", 404);
        var client = await DoctorClientAsync(host);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Doctor/Prescriptions/Print/{RxId}")).StatusCode);
    }

    // ---------- Panel iskeleti ----------

    [Fact]
    public async Task Panel_shell_shows_profile_card_with_name_role_avatar_and_menu()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSupport(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor")).Content.ReadHtmlAsync();

        Assert.Contains("class=\"panel-side", html);
        Assert.Contains("panel-profile", html);
        Assert.Contains("class=\"avatar avatar-lg\"", html);
        Assert.Contains("/Doctor/Patients", html);
        Assert.Contains("/Doctor/Calendar", html);
        Assert.Contains("stat-tile", html);
    }
}
