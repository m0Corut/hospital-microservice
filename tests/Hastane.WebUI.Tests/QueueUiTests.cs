using System.Net;
using System.Text.Json;
using Hastane.TestSupport;

namespace Hastane.WebUI.Tests;

// Sıra (Queue) ekranları: herkese açık çağrı ekranı, hasta "Sıraya gir"/"Sıram", doktor sırası.
public class QueueUiTests
{
    private const string DoctorId = "doc-1";
    private static readonly Guid ApptId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EntryId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private static string? Loc(HttpResponseMessage r) => TestUrls.Relative(r.Headers.Location);

    private static object Entry(int number, string status, string name = "Can Y.", Guid? id = null, int ahead = 0) => new
    {
        id = id ?? Guid.NewGuid(),
        appointmentId = ApptId,
        doctorId = DoctorId,
        displayName = name,
        number,
        status,
        createdAt = DateTime.UtcNow.AddMinutes(-10),
        calledAt = status == "Called" ? DateTime.UtcNow : (DateTime?)null,
        aheadCount = ahead,
    };

    private static object Display(object? current, params object[] next) => new { doctorId = DoctorId, current, next };

    private static void MapDoctors(WebUiTestHost host)
    {
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, new
        {
            items = new[] { new { id = DoctorId, firstName = "Ali", lastName = "Veli", title = "Dr.", branchId = "b1" } },
            totalCount = 1,
            page = 1,
            pageSize = 50,
        });
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { new { id = DoctorId, firstName = "Ali", lastName = "Veli", title = "Dr.", branchId = "b1" } });
    }

    private static async Task<HttpClient> DoctorClientAsync(WebUiTestHost host, string? doctorId = DoctorId)
    {
        var client = host.CreateClient();
        await host.LoginAsync(client, TestJwt.Create("user-1", ["Doctor"], doctorId: doctorId), roles: ["Doctor"]);
        return client;
    }

    // --- Herkese açık çağrı ekranı ---

    [Fact]
    public async Task Display_is_anonymous_200_and_shows_current_and_next_with_masked_names_only()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctors(host);
        host.Gateway.MapJson("GET", "/gateway/queue/display", 200, Display(
            new { number = 12, displayName = "Can Y.", status = "Called", calledAt = DateTime.UtcNow },
            new { number = 13, displayName = "Ayşe K.", status = "Waiting", calledAt = (DateTime?)null }));

        var response = await host.CreateClient().GetAsync($"/Queue/Display?doctorId={DoctorId}");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Şu an çağrılan", html);
        Assert.Contains(">12<", html);
        Assert.Contains("Can Y.", html);
        Assert.Contains("Ayşe K.", html);
        Assert.Contains("Dr. Ali Veli", html);
        // Tam ekran ekran: ortak menü yok; SignalR istemcisi yerel dosyadan, hub adresi ws:// ile Gateway'e.
        Assert.Contains("/lib/microsoft-signalr/signalr.min.js", html);
        Assert.Contains("/js/queue-display.js", html);
        Assert.Matches(@"data-hub-url=""ws://127\.0\.0\.1:\d+/gateway/hubs/queue""", html);
        Assert.Contains("/Queue/DisplayData?doctorId=" + DoctorId, html);
        Assert.DoesNotContain("navbar", html);
        // Anonim ziyaretçi için Gateway'e token gitmedi.
        Assert.All(host.Gateway.Requests.Where(r => r.Path.StartsWith("/gateway/queue", StringComparison.Ordinal)), r => Assert.Equal("", r.Authorization));
    }

    [Fact]
    public async Task Display_without_doctor_shows_picker_and_does_not_call_queue()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctors(host);

        var response = await host.CreateClient().GetAsync("/Queue/Display");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Doktor seçin", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/queue/display");
    }

    [Fact]
    public async Task Display_stays_up_when_queue_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctors(host);
        host.Gateway.MapStatus("GET", "/gateway/queue/display", 503);

        var response = await host.CreateClient().GetAsync($"/Queue/Display?doctorId={DoctorId}");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bağlantı yok", html);
    }

    [Fact]
    public async Task DisplayData_returns_json_without_cache_and_503_when_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/queue/display", 200, Display(
            new { number = 3, displayName = "Can Y.", status = "Called", calledAt = DateTime.UtcNow }));

        var ok = await host.CreateClient().GetAsync($"/Queue/DisplayData?doctorId={DoctorId}");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("no-store", ok.Headers.CacheControl?.ToString());
        using var doc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        Assert.Equal(3, doc.RootElement.GetProperty("current").GetProperty("number").GetInt32());

        host.Gateway.MapStatus("GET", "/gateway/queue/display", 503);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.CreateClient().GetAsync($"/Queue/DisplayData?doctorId={DoctorId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.CreateClient().GetAsync("/Queue/DisplayData")).StatusCode);
    }

    [Fact]
    public async Task Pinned_signalr_client_is_served_locally()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync("/lib/microsoft-signalr/signalr.min.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("HubConnectionBuilder", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Display_hub_url_prefers_public_gateway_address_setting()
    {
        // QueueText.HubUrl'in saf mantığı: ayar varsa o, yoksa sunucunun Gateway adresi; http->ws, https->wss.
        Assert.Equal("ws://localhost:5000/gateway/hubs/queue", ViewModels.Queue.QueueText.HubUrl("http://localhost:5000/", null));
        Assert.Equal("wss://hastane.example/gateway/hubs/queue", ViewModels.Queue.QueueText.HubUrl("http://gateway:80", "https://hastane.example"));
        await Task.CompletedTask;
    }

    // --- Hasta ---

    [Theory]
    [InlineData("/Patient/Queue")]
    public async Task Patient_queue_redirects_anonymous_to_login(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login?returnUrl=", Loc(response));
    }

    [Fact]
    public async Task Patient_queue_is_forbidden_for_doctor_and_admin()
    {
        await using var host = await WebUiTestHost.StartAsync();
        foreach (var role in new[] { "Doctor", "Admin" })
        {
            var client = await host.LoginAsAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Patient/Queue")).StatusCode);
        }
    }

    [Fact]
    public async Task Patient_queue_shows_number_and_people_ahead()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctors(host);
        host.Gateway.MapJson("GET", "/gateway/queue/me/today", 200, new[] { Entry(7, "Waiting", ahead: 2) });
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Patient/Queue");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(">7<", html);
        Assert.Contains("Önünüzde", html);
        Assert.Contains("<strong>2</strong>", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("/js/queue-live.js", html);
        Assert.StartsWith("Bearer ", host.Gateway.Requests.Last(r => r.Path == "/gateway/queue/me/today").Authorization);
    }

    [Fact]
    public async Task Patient_queue_empty_state_and_service_down_warning()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/queue/me/today", 200, Array.Empty<object>());
        var client = await host.LoginAsAsync("Patient");
        Assert.Contains("sıraya girmediniz", await (await client.GetAsync("/Patient/Queue")).Content.ReadHtmlAsync());

        host.Gateway.MapStatus("GET", "/gateway/queue/me/today", 503);
        var down = await client.GetAsync("/Patient/Queue");
        Assert.Equal(HttpStatusCode.OK, down.StatusCode);
        Assert.Contains("ulaşılamıyor", await down.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task CheckIn_post_sends_only_appointment_id_with_token_and_redirects_to_queue()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/queue/check-in", 201, Entry(4, "Waiting"));
        host.Gateway.MapJson("GET", "/gateway/queue/me/today", 200, new[] { Entry(4, "Waiting") });
        MapDoctors(host);
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Patient/Queue/CheckIn", new() { ["appointmentId"] = ApptId.ToString() });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Patient/Queue", Loc(response));
        var sent = host.Gateway.Requests.Single(r => r.Method == "POST" && r.Path == "/gateway/queue/check-in");
        Assert.StartsWith("Bearer ", sent.Authorization);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal(["appointmentId"], body.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(ApptId, body.RootElement.GetProperty("appointmentId").GetGuid());
        Assert.Contains("Sıra numaranız: 4", await (await client.GetAsync("/Patient/Queue")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task CheckIn_conflict_returns_to_appointment_with_api_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.MapProblem("POST", "/gateway/queue/check-in", 409, "Randevunuz bugün için değil; sıraya yalnız randevu gününde girilebilir.");
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, new
        {
            id = ApptId,
            doctorId = DoctorId,
            patientId = "user-1",
            branchId = "b1",
            appointmentDate = DateTime.UtcNow.AddDays(3),
            status = "Confirmed",
            createdAt = DateTime.UtcNow.AddDays(-1),
        });
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Patient/Queue/CheckIn", new() { ["appointmentId"] = ApptId.ToString() });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Appointments/Details/{ApptId}", Loc(response));
        Assert.Contains("yalnız randevu gününde", await (await client.GetAsync(Loc(response)!)).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task CheckIn_without_antiforgery_token_is_rejected_and_never_reaches_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var response = await client.PostAsync("/Patient/Queue/CheckIn", new FormUrlEncodedContent(new Dictionary<string, string> { ["appointmentId"] = ApptId.ToString() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/queue/check-in");
    }

    [Fact]
    public async Task CheckIn_is_forbidden_for_doctor()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Doctor");

        var response = await host.PostAsync(client, "/Patient/Queue/CheckIn", new() { ["appointmentId"] = ApptId.ToString() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/queue/check-in");
    }

    private static object Appointment(string status, DateTime date) => new
    {
        id = ApptId,
        doctorId = DoctorId,
        patientId = "user-1",
        branchId = "b1",
        appointmentDate = date,
        status,
        createdAt = DateTime.UtcNow.AddDays(-1),
    };

    [Theory]
    [InlineData("Confirmed", 0, true)]
    [InlineData("Confirmed", 3, false)]
    [InlineData("Pending", 0, false)]
    [InlineData("Cancelled", 0, false)]
    public async Task Appointment_details_offers_check_in_only_for_confirmed_today(string status, int dayOffset, bool expected)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/appointments/{ApptId}", 200, Appointment(status, DateTime.UtcNow.AddDays(dayOffset)));
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync($"/Patient/Appointments/Details/{ApptId}")).Content.ReadHtmlAsync();

        Assert.Equal(expected, html.Contains("/Patient/Queue/CheckIn", StringComparison.Ordinal));
        Assert.Equal(expected, html.Contains("Sıraya gir", StringComparison.Ordinal));
    }

    // --- Doktor ---

    [Fact]
    public async Task Doctor_queue_redirects_anonymous_and_forbids_patient()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var anon = await host.CreateClient().GetAsync("/Doctor/Queue");
        Assert.Equal(HttpStatusCode.Redirect, anon.StatusCode);
        Assert.StartsWith("/Account/Login?returnUrl=", Loc(anon));

        var patient = await host.LoginAsAsync("Patient");
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.GetAsync("/Doctor/Queue")).StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/queue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Doctor_queue_lists_current_waiting_and_finished()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/queue/doctor/today", 200, new[]
        {
            Entry(1, "Completed", "Eski H."),
            Entry(2, "Called", "Can Y.", EntryId),
            Entry(3, "Waiting", "Ayşe K."),
            Entry(4, "Waiting", "Mehmet T."),
        });
        var client = await DoctorClientAsync(host);

        var response = await client.GetAsync("/Doctor/Queue");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Can Y.", html);
        Assert.Contains("Ayşe K.", html);
        Assert.Contains("Mehmet T.", html);
        Assert.Contains("Eski H.", html);
        Assert.Contains("Sıradakini çağır", html);
        Assert.Contains($"/Doctor/Queue/Complete/{EntryId}", html);
        Assert.Contains($"/Queue/Display?doctorId={DoctorId}", html);
        Assert.Contains($"data-doctor-ids=\"{DoctorId}\"", html);
        // Doktor kimliği sorgudan değil token'dan: istekte doctorId parametresi yok.
        Assert.DoesNotContain(host.Gateway.Requests.Where(r => r.Path == "/gateway/queue/doctor/today"), r => r.Body.Contains(DoctorId));
    }

    [Fact]
    public async Task Doctor_without_doctor_id_sees_not_linked_page_and_no_queue_call()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await DoctorClientAsync(host, doctorId: null);

        var response = await client.GetAsync("/Doctor/Queue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/queue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CallNext_post_goes_to_gateway_with_token_and_redirects_back()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/queue/doctor/call-next", 200, Entry(3, "Called", "Ayşe K."));
        host.Gateway.MapJson("GET", "/gateway/queue/doctor/today", 200, new[] { Entry(3, "Called", "Ayşe K.") });
        var client = await DoctorClientAsync(host);

        var response = await host.PostAsync(client, "/Doctor/Queue/CallNext");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Doctor/Queue", Loc(response));
        var sent = host.Gateway.Requests.Single(r => r.Method == "POST" && r.Path == "/gateway/queue/doctor/call-next");
        Assert.StartsWith("Bearer ", sent.Authorization);
        Assert.Contains("Sıradaki çağrıldı: 3", await (await client.GetAsync("/Doctor/Queue")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task CallNext_conflict_shows_api_message_as_warning()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.MapProblem("POST", "/gateway/queue/doctor/call-next", 409, "Bu doktorun çağrılmış bir hastası var; önce onu tamamlayın ya da 'gelmedi' işaretleyin.");
        host.Gateway.MapJson("GET", "/gateway/queue/doctor/today", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var response = await host.PostAsync(client, "/Doctor/Queue/CallNext");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("çağrılmış bir hastası var", await (await client.GetAsync("/Doctor/Queue")).Content.ReadHtmlAsync());
    }

    [Theory]
    [InlineData("Call", "call")]
    [InlineData("Complete", "complete")]
    [InlineData("NoShow", "no-show")]
    public async Task Entry_actions_post_to_the_matching_gateway_path(string action, string apiSegment)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", $"/gateway/queue/{EntryId}/{apiSegment}", 200, Entry(2, "Called", id: EntryId));
        var client = await DoctorClientAsync(host);

        var response = await host.PostAsync(client, $"/Doctor/Queue/{action}/{EntryId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Doctor/Queue", Loc(response));
        Assert.Single(host.Gateway.Requests, r => r.Method == "POST" && r.Path == $"/gateway/queue/{EntryId}/{apiSegment}");
    }

    [Theory]
    [InlineData("/Doctor/Queue/CallNext")]
    [InlineData("/Doctor/Queue/Call/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")]
    [InlineData("/Doctor/Queue/Complete/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")]
    [InlineData("/Doctor/Queue/NoShow/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")]
    public async Task Doctor_actions_without_antiforgery_token_are_rejected(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await DoctorClientAsync(host);

        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "POST" && r.Path.StartsWith("/gateway/queue", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/Doctor/Queue/CallNext")]
    [InlineData("/Doctor/Queue/Complete/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")]
    public async Task Doctor_actions_are_forbidden_for_patient_even_with_valid_token(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "POST" && r.Path.StartsWith("/gateway/queue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Doctor_action_on_other_doctors_entry_shows_404_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("POST", $"/gateway/queue/{EntryId}/call", 404);
        var client = await DoctorClientAsync(host);

        var response = await host.PostAsync(client, $"/Doctor/Queue/Call/{EntryId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
