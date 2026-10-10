using System.Net;
using System.Text.Json;
using static Hastane.WebUI.Tests.AdminPanelTestHelpers;

namespace Hastane.WebUI.Tests;

// Admin › Arama Dizini (Elasticsearch durumu + yeniden oluştur) ve doktor arama kutusu önerisi. Sahte gateway deseni.
public class DoctorSearchIndexUiTests
{
    private const string StatusPath = "/gateway/doctors/search-index/status";
    private const string RebuildPath = "/gateway/doctors/search-index/rebuild";
    private const string SuggestPath = "/gateway/doctors/suggest";

    private static object Status(bool enabled = true, bool reachable = true, long? indexed = 3, long mongo = 3, bool inSync = true) => new
    {
        enabled,
        reachable,
        index = enabled ? "hastane-doctors" : null,
        indexedCount = indexed,
        mongoCount = mongo,
        inSync,
    };

    // ---- yetki ----

    [Fact]
    public async Task Anonymous_is_redirected_and_patient_doctor_get_403_without_a_gateway_call()
    {
        await using (var anon = await WebUiTestHost.StartAsync())
        {
            var response = await anon.CreateClient().GetAsync("/Admin/SearchIndex");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Account/Login", Location(response));
            Assert.Empty(anon.Gateway.Requests);
        }

        foreach (var role in new[] { "Patient", "Doctor" })
        {
            await using var host = await WebUiTestHost.StartAsync();
            var client = await ClientAsync(host, role);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/SearchIndex")).StatusCode);
            var post = await PostAsync(host, client, "/", "/Admin/SearchIndex/Rebuild");
            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);

            // 403 sayfası menü kabuğunu (şube listesi, rozetler) çizer; iddia: dizin uçlarına hiç gidilmedi.
            Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/doctors/search-index", StringComparison.Ordinal));
        }
    }

    // ---- durum sayfası ----

    [Fact]
    public async Task Status_page_shows_counts_and_a_confirmed_rebuild_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", StatusPath, 200, Status(indexed: 2, mongo: 3, inSync: false));
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/SearchIndex");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Arama dizini", html);
        Assert.Contains("hastane-doctors", html);
        Assert.Contains("Uyuşmuyor", html);
        Assert.Contains("/Admin/SearchIndex/Rebuild", html);
        Assert.Contains("return confirm(", html);
        Assert.StartsWith("Bearer ", Last(host, "GET", StatusPath).Authorization);
    }

    [Fact]
    public async Task Status_page_when_index_is_off_explains_it_and_hides_the_rebuild_button()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", StatusPath, 200, Status(enabled: false, reachable: false, indexed: null, mongo: 5, inSync: false));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/SearchIndex"));

        Assert.Contains("Kapalı", html);
        Assert.DoesNotContain("/Admin/SearchIndex/Rebuild", html);
    }

    [Fact]
    public async Task Status_page_still_opens_when_the_doctor_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", StatusPath, 503);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/SearchIndex");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ulaşılamadı", await HtmlAsync(response));
    }

    // ---- yeniden oluştur ----

    [Fact]
    public async Task Rebuild_posts_with_antiforgery_and_reports_the_count()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", StatusPath, 200, Status());
        host.Gateway.MapJson("POST", RebuildPath, 200, new { indexed = 42 });
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/SearchIndex", "/Admin/SearchIndex/Rebuild");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Admin/SearchIndex", Location(response));
        Assert.StartsWith("Bearer ", Last(host, "POST", RebuildPath).Authorization);
        Assert.Contains("42 doktor yazıldı", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Rebuild_without_antiforgery_token_is_rejected_and_never_reaches_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", RebuildPath, 200, new { indexed = 1 });
        var client = await AdminClientAsync(host);

        var response = await client.PostAsync("/Admin/SearchIndex/Rebuild", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(Called(host, "POST", RebuildPath));
    }

    [Theory]
    [InlineData(409, "kapalı")]
    [InlineData(503, "ulaşılamadı")]
    public async Task Rebuild_failures_become_a_warning(int status, string expectedText)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", StatusPath, 200, Status());
        host.MapProblem("POST", RebuildPath, status, "Arama dizini");
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/SearchIndex", "/Admin/SearchIndex/Rebuild");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(expectedText, await HtmlAsync(await client.GetAsync(Location(response))));
    }

    // ---- arama kutusu önerisi ----

    [Fact]
    public async Task Suggest_is_anonymous_forwards_the_query_and_returns_texts_only()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var query = MapCapturingQuery(host, "GET", SuggestPath, 200, new object[]
        {
            new { text = "Mehmet Yılmaz", type = "doctor", doctorId = "d1" },
            new { text = "Çocuk Sağlığı", type = "service", doctorId = (string?)null },
            new { text = "Mehmet Yılmaz", type = "doctor", doctorId = "d1" },
        });

        var response = await host.CreateClient().GetAsync("/doctor-suggest?q=yilm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("?q=yilm", query());
        var items = JsonSerializer.Deserialize<string[]>(await response.Content.ReadAsStringAsync());
        Assert.Equal(["Mehmet Yılmaz", "Çocuk Sağlığı"], items!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("%20")]
    public async Task Suggest_with_empty_query_does_not_call_the_gateway(string q)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync("/doctor-suggest?q=" + q);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", (await response.Content.ReadAsStringAsync()).Trim());
        Assert.False(Called(host, "GET", SuggestPath));
    }

    [Fact]
    public async Task Suggest_returns_an_empty_list_when_the_doctor_service_fails()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", SuggestPath, 503);

        var response = await host.CreateClient().GetAsync("/doctor-suggest?q=ali");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", (await response.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task Doctor_search_box_is_wired_to_the_suggest_endpoint()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 10 });

        var html = await HtmlAsync(await host.CreateClient().GetAsync("/Doctors"));

        Assert.Contains("list=\"q-suggest\"", html);
        Assert.Contains("<datalist id=\"q-suggest\">", html);
        Assert.Contains("/doctor-suggest?q=", html);
    }
}
