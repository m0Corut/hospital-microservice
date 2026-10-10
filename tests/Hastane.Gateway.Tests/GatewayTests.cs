using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace Hastane.Gateway.Tests;

public sealed class GatewayTests : IAsyncLifetime
{
    private FakeDownstream _fake = null!;
    private GatewayFactory _factory = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _fake = await FakeDownstream.StartAsync();
        _factory = new GatewayFactory(_fake.Port);
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _fake.DisposeAsync();
    }

    // Gerçek controller [Route] değerleri (koddan okundu): üst önek -> alt önek.
    private static readonly (string Up, string Down)[] Expected =
    [
        ("auth", "/api/auth"), ("users", "/api/users"), ("roles", "/api/roles"),
        ("branches", "/api/branches"), ("doctors", "/api/doctors"), ("reviews", "/api/reviews"),
        ("appointments", "/api/appointments"), ("prescriptions", "/api/prescriptions"),
        ("payments", "/api/payments"), ("invoices", "/api/invoices"),
        ("doctor-applications", "/api/doctor-applications"), ("patients", "/api/patients"),
        ("notifications", "/api/notifications"), ("chat", "/api/chat"),
    ];

    public static TheoryData<string, string, string> PathCases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (up, down) in Expected)
        {
            data.Add($"/gateway/{up}", "", down);
            data.Add($"/gateway/{up}/", "", down);
            data.Add($"/gateway/{up}/abc123?branchId=7&x=a%20b", "?branchId=7&x=a%20b", $"{down}/abc123");
            data.Add($"/gateway/{up}/abc123/roles/Admin", "", $"{down}/abc123/roles/Admin");
        }

        return data;
    }

    [Fact]
    public void Route_table_matches_ocelot_json_exactly()
    {
        var actual = OcelotRoutes.Read().Select(r => (r.Upstream, r.Downstream)).Order().ToArray();
        var expected = Expected.Select(e => ($"/gateway/{e.Up}/{{everything}}", $"{e.Down}/{{everything}}")).Order().ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Every_route_allows_all_http_methods()
    {
        string[] all = ["GET", "POST", "PUT", "PATCH", "DELETE"];
        foreach (var r in OcelotRoutes.Read())
        {
            Assert.Equal(all.Order(), r.Methods.Order());
        }
    }

    [Theory]
    [MemberData(nameof(PathCases))]
    public async Task Request_reaches_correct_downstream_path_and_query(string url, string expectedQuery, string expectedPath)
    {
        var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = (await response.Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;
        Assert.Equal(expectedQuery, echo.Query);
        // Kök koleksiyonda sondaki "/" olsa da olmasa da MVC aynı uca çözer; yalnız onu yok sayarız.
        Assert.Equal(expectedPath, echo.Path.TrimEnd('/'));
    }

    [Fact]
    public async Task Authorization_header_is_forwarded_untouched()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/gateway/doctors");
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer x");

        var echo = (await (await _client.SendAsync(req, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;

        Assert.Equal("Bearer x", echo.Authorization);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Methods_and_body_are_forwarded(string method)
    {
        using var req = new HttpRequestMessage(new HttpMethod(method), "/gateway/appointments/42")
        {
            Content = new StringContent("{\"a\":1}", Encoding.UTF8, "application/json"),
        };

        var echo = (await (await _client.SendAsync(req, TestContext.Current.CancellationToken))
            .Content.ReadFromJsonAsync<Echo>(TestContext.Current.CancellationToken))!;

        Assert.Equal(method, echo.Method);
        Assert.Equal("/api/appointments/42", echo.Path);
        Assert.Equal("{\"a\":1}", echo.Body);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task Downstream_status_codes_pass_through(int status)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/gateway/prescriptions/1");
        req.Headers.Add("X-Fake-Status", status.ToString());

        var response = await _client.SendAsync(req, TestContext.Current.CancellationToken);

        Assert.Equal(status, (int)response.StatusCode);
    }

    [Fact]
    public async Task Unknown_path_returns_404()
    {
        var response = await _client.GetAsync("/gateway/yok", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_returns_200_Healthy()
    {
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Downstream_down_returns_5xx_without_stack_trace()
    {
        await using var factory = new GatewayFactory(OcelotRoutes.FreePort());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/gateway/branches", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.InRange((int)response.StatusCode, 500, 599);
        Assert.DoesNotContain(body.Split('\n'), line => line.TrimStart().StartsWith("at ", StringComparison.Ordinal));
    }
}
