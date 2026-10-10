using System.Net;
using static Hastane.WebUI.Tests.AdminPanelTestHelpers;

namespace Hastane.WebUI.Tests;

// Admin › Analitik (Kafka etkinlik akışından günlük toplamlar + yeniden hesapla). Sahte gateway deseni.
public class AnalyticsUiTests
{
    private const string StatusPath = "/gateway/analytics/status";
    private const string ViewedPath = "/gateway/analytics/doctors/top-viewed";
    private const string TermsPath = "/gateway/analytics/search/top-terms";
    private const string RebuildPath = "/gateway/analytics/rebuild";
    private const string BatchPath = "/gateway/doctors/batch";

    private static object Status(bool enabled = true, bool connected = true, long today = 12, DateTime? last = null) => new
    {
        kafkaEnabled = enabled,
        kafkaConnected = connected,
        topic = enabled ? "hastane.doctor-activity" : null,
        groupId = enabled ? "analytics" : null,
        lastProcessedAt = last ?? new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc),
        processedToday = today,
    };

    private static void MapData(WebUiTestHost host, bool batchOk = true)
    {
        host.Gateway.MapJson("GET", StatusPath, 200, Status());
        host.Gateway.MapJson("GET", ViewedPath, 200, new object[]
        {
            new { doctorId = "d1", views = 40L },
            new { doctorId = "d2", views = 10L },
        });
        host.Gateway.MapJson("GET", TermsPath, 200, new object[]
        {
            new { term = "kardiyoloji", count = 7L },
            new { term = "cocuk saglik", count = 3L },
        });
        if (batchOk)
            host.Gateway.MapJson("GET", BatchPath, 200, new object[]
            {
                new { id = "d1", firstName = "Ali", lastName = "Veli", title = "Dr.", branchId = "b1" },
                new { id = "d2", firstName = "Ayşe", lastName = "Kaya", title = "Uzm. Dr.", branchId = "b1" },
            });
        else
            host.Gateway.MapStatus("GET", BatchPath, 503);
    }

    // ---- yetki ----

    [Fact]
    public async Task Anonymous_is_redirected_and_patient_doctor_get_403_without_a_gateway_call()
    {
        await using (var anon = await WebUiTestHost.StartAsync())
        {
            var response = await anon.CreateClient().GetAsync("/Admin/Analytics");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Account/Login", Location(response));
            Assert.Empty(anon.Gateway.Requests);
        }

        foreach (var role in new[] { "Patient", "Doctor" })
        {
            await using var host = await WebUiTestHost.StartAsync();
            var client = await ClientAsync(host, role);
            var before = host.Gateway.Requests.Count;

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Analytics")).StatusCode);
            var post = await PostAsync(host, client, "/", "/Admin/Analytics/Rebuild");
            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);

            Assert.Equal(before, host.Gateway.Requests.Count);
        }
    }

    // ---- sayfa ----

    [Fact]
    public async Task Page_shows_status_top_doctors_with_names_bars_and_top_terms()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapData(host);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Analytics");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Analitik", html);
        Assert.Contains("Açık, bağlı", html);
        Assert.Contains("hastane.doctor-activity", html);
        Assert.Contains("12", html); // bugün işlenen
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("Uzm. Dr. Ayşe Kaya", html);
        Assert.Contains("width: 100%", html); // en çok görüntülenen = tam çubuk
        Assert.Contains("width: 25%", html);  // 10/40
        Assert.Contains("kardiyoloji", html);
        Assert.Contains("cocuk saglik", html);
        Assert.StartsWith("Bearer ", Last(host, "GET", ViewedPath).Authorization);
    }

    [Fact]
    public async Task Page_asks_for_ten_doctors_and_twenty_terms_for_the_chosen_window()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", StatusPath, 200, Status());
        host.Gateway.MapJson("GET", BatchPath, 200, Array.Empty<object>());
        var viewedQuery = MapCapturingQuery(host, "GET", ViewedPath, 200, Array.Empty<object>());
        var termsQuery = MapCapturingQuery(host, "GET", TermsPath, 200, Array.Empty<object>());
        var client = await AdminClientAsync(host);

        await client.GetAsync("/Admin/Analytics?days=30");

        Assert.Equal("?days=30&take=10", viewedQuery());
        Assert.Equal("?days=30&take=20", termsQuery());
    }

    [Fact]
    public async Task Unsupported_window_falls_back_to_seven_days()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", StatusPath, 200, Status());
        var viewedQuery = MapCapturingQuery(host, "GET", ViewedPath, 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", TermsPath, 200, Array.Empty<object>());
        var client = await AdminClientAsync(host);

        await client.GetAsync("/Admin/Analytics?days=9999");

        Assert.Equal("?days=7&take=10", viewedQuery());
    }

    [Fact]
    public async Task Empty_lists_show_friendly_messages()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", StatusPath, 200, Status(today: 0));
        host.Gateway.MapJson("GET", ViewedPath, 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", TermsPath, 200, Array.Empty<object>());
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Analytics"));

        Assert.Contains("profil görüntülemesi yok", html);
        Assert.Contains("arama yok", html);
        Assert.False(Called(host, "GET", BatchPath)); // listede doktor yok: ad çağrısı da yok
    }

    [Fact]
    public async Task Doctor_names_fall_back_when_the_doctor_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapData(host, batchOk: false);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Analytics");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bilinmeyen doktor", html);
        Assert.Contains("kardiyoloji", html); // terimler yine görünür
    }

    // ---- Kafka / servis durumu ----

    [Fact]
    public async Task Kafka_off_is_explained_and_the_rebuild_button_is_hidden()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", StatusPath, 200, Status(enabled: false, connected: false, today: 0));
        host.Gateway.MapJson("GET", ViewedPath, 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", TermsPath, 200, Array.Empty<object>());
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Analytics"));

        Assert.Contains("Kapalı", html);
        Assert.DoesNotContain("/Admin/Analytics/Rebuild", html);
    }

    [Fact]
    public async Task Kafka_configured_but_not_connected_is_flagged_and_still_shows_old_totals()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapData(host);
        host.Gateway.MapJson("GET", StatusPath, 200, Status(connected: false));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Analytics"));

        Assert.Contains("Bağlı değil", html);
        Assert.Contains("kardiyoloji", html);
    }

    [Fact]
    public async Task Page_still_opens_with_a_warning_when_the_analytics_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", StatusPath, 503);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Analytics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ulaşılamadı", await HtmlAsync(response));
        Assert.False(Called(host, "GET", ViewedPath));
        Assert.False(Called(host, "GET", TermsPath));
    }

    [Fact]
    public async Task Page_still_opens_when_the_analytics_route_is_missing_404()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", StatusPath, 404);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Analytics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- yeniden hesapla ----

    [Fact]
    public async Task Rebuild_form_asks_for_confirmation_and_names_the_retention_limit()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapData(host);
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Analytics"));

        Assert.Contains("/Admin/Analytics/Rebuild", html);
        Assert.Contains("return confirm(", html);
        Assert.Contains("7 gün", html);
    }

    [Fact]
    public async Task Rebuild_posts_with_antiforgery_and_reports_the_result()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapData(host);
        host.Gateway.MapJson("POST", RebuildPath, 200, new { partitionsRewound = 3, aggregatesDeleted = 25 });
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/Analytics", "/Admin/Analytics/Rebuild");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Admin/Analytics", Location(response));
        Assert.StartsWith("Bearer ", Last(host, "POST", RebuildPath).Authorization);
        var html = await HtmlAsync(await client.GetAsync(Location(response)));
        Assert.Contains("3 bölüm başa sarıldı", html);
        Assert.Contains("25 toplam kaydı", html);
    }

    [Fact]
    public async Task Rebuild_without_antiforgery_token_is_rejected_and_never_reaches_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", RebuildPath, 200, new { partitionsRewound = 1, aggregatesDeleted = 0 });
        var client = await AdminClientAsync(host);

        var response = await client.PostAsync("/Admin/Analytics/Rebuild", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(Called(host, "POST", RebuildPath));
    }

    [Theory]
    [InlineData(409, "Kafka'ya bağlı değil", "bağlı değil")]
    [InlineData(503, "Analitik servisine", "ulaşılamadı")]
    public async Task Rebuild_failures_become_a_warning(int status, string detail, string expectedText)
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapData(host);
        host.MapProblem("POST", RebuildPath, status, detail);
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/Analytics", "/Admin/Analytics/Rebuild");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(expectedText, await HtmlAsync(await client.GetAsync(Location(response))));
    }
}
