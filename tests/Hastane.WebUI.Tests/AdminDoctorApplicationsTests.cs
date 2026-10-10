using System.Net;
using static Hastane.WebUI.Tests.AdminPanelTestHelpers;

namespace Hastane.WebUI.Tests;

// Admin › Doktor Başvuruları: liste + durum süzgeci + onay/red + menü rozeti. Sahte gateway deseni (UI-KURALLARI §7).
public class AdminDoctorApplicationsTests
{
    private const string Path = "/gateway/doctor-applications";
    private static readonly Guid AppId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static object Application(string status = "Pending", string? decisionNote = null) => new
    {
        id = AppId,
        userId = "user-9",
        firstName = "Ali",
        lastName = "Veli",
        email = "ali@test.local",
        branchId = "b0",
        licenseNumber = "TR-12345",
        note = "Deneyimliyim",
        status,
        createdAt = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc),
        decidedAt = (DateTime?)null,
        decisionNote,
    };

    private static async Task<(WebUiTestHost Host, HttpClient Client)> StartAsync(string status = "Pending", int total = 1)
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", Path, 200, Paged([Application(status)], page: 1, pageSize: 20, total: total));
        host.Gateway.MapJson("GET", "/gateway/users", 200, Paged([], page: 1, pageSize: 20, total: 0));
        return (host, await AdminClientAsync(host));
    }

    // Sayfa hem listeyi hem menü rozetini aynı yoldan ister: tüm sorgu dizeleri sırayla toplanır.
    private static System.Collections.Concurrent.ConcurrentQueue<string> CaptureQueries(WebUiTestHost host, object body)
    {
        var queries = new System.Collections.Concurrent.ConcurrentQueue<string>();
        host.Gateway.Map("GET", Path, ctx =>
        {
            queries.Enqueue(ctx.Request.QueryString.Value ?? "");
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsJsonAsync(body);
        });
        return queries;
    }

    [Theory]
    [InlineData("/Admin/DoctorApplications")]
    public async Task Anonymous_is_redirected_and_patient_doctor_get_403_without_a_gateway_call(string path)
    {
        await using (var anon = await WebUiTestHost.StartAsync())
        {
            var response = await anon.CreateClient().GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Account/Login", Location(response));
            Assert.Empty(anon.Gateway.Requests);
        }

        foreach (var role in new[] { "Patient", "Doctor" })
        {
            await using var host = await WebUiTestHost.StartAsync();
            var client = await ClientAsync(host, role);
            var before = host.Gateway.Requests.Count;

            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(before, host.Gateway.Requests.Count);
        }
    }

    [Theory]
    [InlineData("/Admin/DoctorApplications/Approve")]
    [InlineData("/Admin/DoctorApplications/Reject")]
    public async Task Patient_and_doctor_cannot_post_decisions(string postPath)
    {
        foreach (var role in new[] { "Patient", "Doctor" })
        {
            await using var host = await WebUiTestHost.StartAsync();
            var client = await ClientAsync(host, role);

            var response = await PostAsync(host, client, "/", postPath, new() { ["id"] = AppId.ToString(), ["note"] = "x" });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "POST" && r.Path.StartsWith(Path, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task List_shows_the_row_with_branch_name_and_forwards_the_paging_query()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var queries = CaptureQueries(host, Paged([Application()], page: 1, pageSize: 20, total: 1));

        var response = await client.GetAsync("/Admin/DoctorApplications");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ali Veli", html);
        Assert.Contains("ali@test.local", html);
        Assert.Contains("Kardiyoloji", html); // kimlik değil branş adı
        Assert.Contains("TR-12345", html);
        Assert.Contains("Deneyimliyim", html);
        Assert.Contains("05.10.2026", html);
        Assert.Contains("Bekliyor", html);
        Assert.Contains("?page=1&pageSize=20", queries); // liste; "pageSize=1" olan sorgu menü rozeti
        Assert.StartsWith("Bearer ", Last(host, "GET", Path).Authorization);
    }

    [Fact]
    public async Task Status_filter_is_forwarded_and_unknown_values_are_dropped()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var queries = CaptureQueries(host, Paged([], page: 1, pageSize: 20, total: 0));

        await client.GetAsync("/Admin/DoctorApplications?status=Rejected");
        Assert.Contains("?page=1&pageSize=20&status=Rejected", queries);

        queries.Clear();
        await client.GetAsync("/Admin/DoctorApplications?status=Hack%26x=1");
        Assert.Contains("?page=1&pageSize=20", queries); // bilinmeyen değer süzgeç olmaz
        Assert.DoesNotContain(queries, q => q.Contains("Hack", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pending_rows_have_approve_and_reject_forms_decided_rows_do_not()
    {
        var (pending, pendingClient) = await StartAsync("Pending");
        await using (pending)
        {
            var html = await HtmlAsync(await pendingClient.GetAsync("/Admin/DoctorApplications"));
            Assert.Contains("/Admin/DoctorApplications/Approve", html);
            Assert.Contains("/Admin/DoctorApplications/Reject", html);
            Assert.Contains("name=\"note\"", html);
            Assert.Contains("required", html);
        }

        var (approved, approvedClient) = await StartAsync("Approved");
        await using (approved)
        {
            var html = await HtmlAsync(await approvedClient.GetAsync("/Admin/DoctorApplications"));
            Assert.DoesNotContain("/Admin/DoctorApplications/Approve", html);
            Assert.DoesNotContain("/Admin/DoctorApplications/Reject", html);
        }
    }

    [Fact]
    public async Task Approve_posts_with_antiforgery_and_then_suggests_creating_and_linking_the_doctor()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        host.Gateway.MapJson("POST", $"{Path}/{AppId}/approve", 200, Application("Approved"));

        var response = await PostAsync(host, client, "/Admin/DoctorApplications", "/Admin/DoctorApplications/Approve",
            new() { ["id"] = AppId.ToString() });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Admin/DoctorApplications", Location(response));
        Assert.StartsWith("Bearer ", Last(host, "POST", $"{Path}/{AppId}/approve").Authorization);

        var html = await HtmlAsync(await client.GetAsync(Location(response)));
        Assert.Contains("Başvuru onaylandı", html);
        Assert.Contains("Şimdi doktor kaydını oluşturun ve bu kullanıcıya bağlayın", html);
        Assert.Contains("href=\"/Admin/Doctors/Create\"", html);
        Assert.Contains("href=\"/Admin/Users/Details/user-9\"", html);

        // öneri tek seferliktir: sayfa yenilenince kaybolur
        Assert.DoesNotContain("Şimdi doktor kaydını oluşturun", await HtmlAsync(await client.GetAsync("/Admin/DoctorApplications")));
    }

    [Fact]
    public async Task Approve_without_antiforgery_token_is_rejected()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;

        var response = await client.PostAsync("/Admin/DoctorApplications/Approve",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = AppId.ToString() }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "POST" && r.Path.EndsWith("/approve", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Approve_conflict_409_says_a_decision_was_already_made()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        host.MapProblem("POST", $"{Path}/{AppId}/approve", 409, "Bu başvuru için zaten karar verilmiş.");

        var response = await PostAsync(host, client, "/Admin/DoctorApplications", "/Admin/DoctorApplications/Approve",
            new() { ["id"] = AppId.ToString() });

        var html = await HtmlAsync(await client.GetAsync(Location(response)));
        Assert.Contains("zaten karar verilmiş", html);
        Assert.DoesNotContain("Şimdi doktor kaydını oluşturun", html);
    }

    [Fact]
    public async Task Reject_without_a_note_is_refused_before_any_service_call()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;

        var response = await PostAsync(host, client, "/Admin/DoctorApplications", "/Admin/DoctorApplications/Reject",
            new() { ["id"] = AppId.ToString(), ["note"] = "   " });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "POST" && r.Path.EndsWith("/reject", StringComparison.Ordinal));
        Assert.Contains("Reddetmek için bir not yazın", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Reject_posts_the_trimmed_note_and_reports_success()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        host.Gateway.MapJson("POST", $"{Path}/{AppId}/reject", 200, Application("Rejected", "Lisans doğrulanamadı."));

        var response = await PostAsync(host, client, "/Admin/DoctorApplications", "/Admin/DoctorApplications/Reject",
            new() { ["id"] = AppId.ToString(), ["note"] = "  Lisans doğrulanamadı.  ", ["status"] = "Pending", ["page"] = "2" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("status=Pending", Location(response));
        Assert.Contains("page=2", Location(response));
        Assert.Equal("Lisans doğrulanamadı.", Json(Last(host, "POST", $"{Path}/{AppId}/reject")).GetProperty("note").GetString());
        Assert.Contains("Başvuru reddedildi", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Reject_conflict_409_says_a_decision_was_already_made()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        host.MapProblem("POST", $"{Path}/{AppId}/reject", 409, "Bu başvuru için zaten karar verilmiş.");

        var response = await PostAsync(host, client, "/Admin/DoctorApplications", "/Admin/DoctorApplications/Reject",
            new() { ["id"] = AppId.ToString(), ["note"] = "Olmaz" });

        Assert.Contains("zaten karar verilmiş", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Menu_shows_a_pending_count_badge_from_the_total_count()
    {
        var (host, client) = await StartAsync(total: 3);
        await using var _ = host;
        var queries = CaptureQueries(host, Paged([Application()], page: 1, pageSize: 1, total: 3));

        var html = await HtmlAsync(await client.GetAsync("/Admin/Users"));

        Assert.Contains("Doktor Başvuruları (3)", html);
        Assert.Contains("/Admin/DoctorApplications", html);
        Assert.Equal(["?page=1&pageSize=1&status=Pending"], queries); // yalnız rozet sorgusu: bekleyenler, tek satır
    }

    [Fact]
    public async Task Menu_has_no_badge_when_the_service_is_down_and_pages_still_open()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", Path, 503);
        host.Gateway.MapJson("GET", "/gateway/users", 200, Paged([], page: 1, pageSize: 20, total: 0));
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Users");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Doktor Başvuruları", html);
        Assert.DoesNotContain("Doktor Başvuruları (", html);
    }

    [Fact]
    public async Task Menu_has_no_badge_when_nothing_is_pending()
    {
        var (host, client) = await StartAsync(total: 0);
        await using var _ = host;

        var html = await HtmlAsync(await client.GetAsync("/Admin/Users"));

        Assert.Contains("Doktor Başvuruları", html);
        Assert.DoesNotContain("Doktor Başvuruları (", html);
    }
}
