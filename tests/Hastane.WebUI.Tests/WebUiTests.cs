using System.Net;
using Hastane.TestSupport;

namespace Hastane.WebUI.Tests;

public class LoginTests
{
    private static string? Loc(HttpResponseMessage r) => TestUrls.Relative(r.Headers.Location);

    [Fact]
    public async Task Login_success_sets_httponly_cookie_and_forwards_bearer_to_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        var token = WebUiTestHost.NewToken("Patient");
        var client = host.CreateClient();

        var login = await host.LoginAsync(client, token);

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var cookie = WebUiTestHost.AuthSetCookie(login);
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure;", cookie + ";", StringComparison.OrdinalIgnoreCase); // yerelde http: SameAsRequest

        // Çerez ömrü token ömrü kadar (30 dk), kayan süre yok.
        var expires = DateTimeOffset.Parse(cookie.Split(';').Select(p => p.Trim())
            .First(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))["expires=".Length..]);
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddMinutes(28), DateTimeOffset.UtcNow.AddMinutes(31));

        // Sonraki istek: sunucu çerezden token'ı alıp gateway'e Bearer olarak taşır.
        var home = await client.GetAsync("/");
        var html = await home.Content.ReadHtmlAsync();
        var branchCall = host.Gateway.Requests.Last(r => r.Path == "/gateway/branches");
        Assert.Equal("Bearer " + token, branchCall.Authorization);
        Assert.Contains("Ayşe Yılmaz", html);

        // Token tarayıcıya hiçbir yerde düz metin gitmez.
        Assert.DoesNotContain(token, html);
        Assert.DoesNotContain(token, await login.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Anonymous_request_sends_no_authorization_header()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");

        await host.CreateClient().GetAsync("/");

        Assert.Equal("", host.Gateway.Requests.Last(r => r.Path == "/gateway/branches").Authorization);
    }

    public static TheoryData<string> BadTokens() => new()
    {
        "broken-signature", "expired", "alg-none", "hs256-public-key", "wrong-issuer",
    };

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task Login_is_rejected_when_gateway_returns_untrustworthy_token(string kind)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var token = kind switch
        {
            "broken-signature" => TestJwt.CreateWithBrokenSignature("user-1", ["Admin"]),
            "expired" => TestJwt.CreateExpired("user-1", ["Admin"]),
            "alg-none" => TestJwt.CreateAlgNone("user-1", ["Admin"]),
            "hs256-public-key" => TestJwt.CreateHs256WithPublicKeyAsSecret("user-1", ["Admin"]),
            _ => TestJwt.Create("user-1", ["Admin"], issuer: "evil-issuer"),
        };
        host.Gateway.MapLogin(token, roles: "Admin");
        var client = host.CreateClient();

        var response = await WebUiTestHost.PostLoginFormAsync(client, "a@b.com", "Sifre123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // yönlendirme yok: giriş sayfası hatayla yeniden çizildi
        Assert.Null(WebUiTestHost.AuthSetCookie(response));
        Assert.Contains("Giriş şu anda tamamlanamıyor", await response.Content.ReadHtmlAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/probe/whoami")).StatusCode); // hâlâ anonim
    }

    [Fact]
    public async Task Wrong_password_shows_generic_message_without_echoing_server_text()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.MapProblem("POST", "/gateway/auth/login", 401, "SUNUCU-DETAYI-SIZMAMALI");

        var response = await WebUiTestHost.PostLoginFormAsync(host.CreateClient(), "a@b.com", "Yanlis123");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("E-posta veya şifre hatalı", html);
        Assert.DoesNotContain("SUNUCU-DETAYI-SIZMAMALI", html);
        Assert.Null(WebUiTestHost.AuthSetCookie(response));
    }

    [Fact]
    public async Task Locked_account_shows_lockout_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.MapProblem("POST", "/gateway/auth/login", 423, "kilit");

        var response = await WebUiTestHost.PostLoginFormAsync(host.CreateClient(), "a@b.com", "Yanlis123");

        Assert.Contains("Hesap geçici olarak kilitlendi", await response.Content.ReadHtmlAsync());
        Assert.Null(WebUiTestHost.AuthSetCookie(response));
    }

    [Theory]
    [InlineData("https://evil.com", "/")]
    [InlineData("//evil.com", "/")]
    [InlineData("http://localhost:5010/", "/")]
    [InlineData("/Patient", "/Patient")]
    public async Task ReturnUrl_only_local_addresses_are_followed(string returnUrl, string expectedLocation)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.LoginAsync(host.CreateClient(), WebUiTestHost.NewToken("Patient"), returnUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedLocation, Loc(response));
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected_with_400()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapLogin(WebUiTestHost.NewToken("Patient"));

        var response = await host.CreateClient().PostAsync("/Account/Login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "a@b.com", ["Password"] = "Sifre123" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/auth/login"); // gateway'e hiç gitmedi
    }

    [Fact]
    public async Task Logout_is_post_only_and_clears_session()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = host.CreateClient();
        await host.LoginAsync(client, WebUiTestHost.NewToken("Patient"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/probe/whoami")).StatusCode);

        // GET ile çıkış yapılamaz.
        var get = await client.GetAsync("/Account/Logout");
        Assert.NotEqual(HttpStatusCode.Redirect, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/probe/whoami")).StatusCode);

        var html = await (await client.GetAsync("/")).Content.ReadHtmlAsync();
        var post = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = WebUiTestHost.ExtractAntiforgeryToken(html) }));

        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/probe/whoami")).StatusCode);
    }

    [Fact]
    public async Task Register_success_redirects_to_login_with_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.MapJson201("/gateway/auth/register");
        var client = host.CreateClient();

        var response = await host.PostFormAsync(client, "/Account/Register", new()
        {
            ["FirstName"] = "Ayşe",
            ["LastName"] = "Yılmaz",
            ["Email"] = "ayse@test.local",
            ["Password"] = "Sifre123",
            ["ConfirmPassword"] = "Sifre123",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var login = await client.GetAsync(response.Headers.Location);
        Assert.Contains("Kayıt tamamlandı", await login.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Register_validation_problem_from_api_lands_in_form_errors()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/auth/register", 400, new
        {
            status = 400,
            title = "Doğrulama hatası",
            errors = new Dictionary<string, string[]> { ["PasswordRequiresUpper"] = ["Şifre büyük harf içermeli."] },
        });

        var response = await host.PostFormAsync(host.CreateClient(), "/Account/Register", new()
        {
            ["FirstName"] = "Ayşe",
            ["LastName"] = "Yılmaz",
            ["Email"] = "ayse@test.local",
            ["Password"] = "sifre1234",
            ["ConfirmPassword"] = "sifre1234",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Şifre büyük harf içermeli.", await response.Content.ReadHtmlAsync());
    }
}

public class GatewayFailureTests
{
    private static string? Loc(HttpResponseMessage r) => TestUrls.Relative(r.Headers.Location);

    [Fact]
    public async Task Api_401_signs_out_and_redirects_to_login_with_returnUrl()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/probe/401", 401);
        var client = host.CreateClient();
        await host.LoginAsync(client, WebUiTestHost.NewToken("Patient"));

        var response = await client.GetAsync("/probe/call/401");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login?returnUrl=%2Fprobe%2Fcall%2F401", Loc(response));
        var cleared = WebUiTestHost.AuthSetCookie(response);
        Assert.NotNull(cleared); // çerez silindi
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/probe/whoami")).StatusCode);
    }

    [Theory]
    [InlineData(403, "Yetkiniz yok")]
    [InlineData(404, "Sayfa bulunamadı")]
    [InlineData(503, "Servis şu an yanıt vermiyor")]
    [InlineData(500, "Servis şu an yanıt vermiyor")]
    public async Task Api_failures_become_friendly_pages(int apiStatus, string expectedText)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", $"/gateway/probe/{apiStatus}", apiStatus);
        var client = host.CreateClient();
        await host.LoginAsync(client, WebUiTestHost.NewToken("Patient"));

        var response = await client.GetAsync($"/probe/call/{apiStatus}");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(apiStatus == 500 ? HttpStatusCode.ServiceUnavailable : (HttpStatusCode)apiStatus, response.StatusCode);
        Assert.Contains(expectedText, html);
        AssertNoStackTrace(html);
    }

    [Fact]
    public async Task Home_page_opens_with_warning_when_gateway_is_down()
    {
        await using var host = WebUiTestHost.StartWithDeadGateway();

        var response = await host.CreateClient().GetAsync("/");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Branş listesi şu anda yüklenemiyor", html);
        AssertNoStackTrace(html);
    }

    [Fact]
    public async Task Other_pages_show_service_down_page_when_gateway_is_down()
    {
        await using var host = WebUiTestHost.StartWithDeadGateway();
        var client = host.CreateClient();
        var response = await WebUiTestHost.PostLoginFormAsync(client, "a@b.com", "Sifre123");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Servis şu an yanıt vermiyor", html);
        AssertNoStackTrace(html);
    }

    [Fact]
    public async Task Home_page_lists_branches_from_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji", "Nöroloji");

        var html = await (await host.CreateClient().GetAsync("/")).Content.ReadHtmlAsync();

        Assert.Contains("Kardiyoloji", html);
        Assert.Contains("Nöroloji", html);
    }

    [Fact]
    public async Task Unhandled_exception_shows_500_page_without_stack_trace_even_in_development()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync("/probe/boom");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("Bir şeyler ters gitti", html);
        Assert.DoesNotContain("GIZLI-YIGIN-IZI", html);
        AssertNoStackTrace(html);
    }

    [Fact]
    public async Task Unknown_page_shows_404_page()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync("/yok/boyle/bir/sayfa");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Sayfa bulunamadı", await response.Content.ReadHtmlAsync());
    }

    private static void AssertNoStackTrace(string html)
    {
        Assert.DoesNotContain("Exception", html);
        Assert.DoesNotContain("StackTrace", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at Hastane.", html);
        Assert.DoesNotContain("127.0.0.1", html); // iç adres sızmaz
    }
}

public class AuthorizationTests
{
    private static string? Loc(HttpResponseMessage r) => TestUrls.Relative(r.Headers.Location);

    [Theory]
    [InlineData("/Admin")]
    [InlineData("/Doctor")]
    [InlineData("/Patient")]
    public async Task Anonymous_is_redirected_to_login_for_areas(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Account/Login?returnUrl=%2F{path.TrimStart('/')}", Loc(response));
    }

    [Theory]
    [InlineData("Patient", "/Admin")]
    [InlineData("Patient", "/Doctor")]
    [InlineData("Doctor", "/Admin")]
    [InlineData("Doctor", "/Patient")]
    [InlineData("Admin", "/Doctor")]
    public async Task Wrong_role_gets_403_page(string role, string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = host.CreateClient();
        await host.LoginAsync(client, WebUiTestHost.NewToken(role), roles: [role]);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Yetkiniz yok", await response.Content.ReadHtmlAsync());
    }

    [Theory]
    [InlineData("Admin", "/Admin")]
    [InlineData("Patient", "/Patient")]
    public async Task Right_role_opens_area(string role, string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = host.CreateClient();
        await host.LoginAsync(client, WebUiTestHost.NewToken(role), roles: [role]);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("yakında", await response.Content.ReadHtmlAsync());
    }

    [Theory]
    [InlineData("Admin", "Yönetim", "Randevularım")]
    [InlineData("Doctor", "Doktor Paneli", "Yönetim")]
    [InlineData("Patient", "Randevularım", "Yönetim")]
    public async Task Role_menu_shows_only_own_links(string role, string visible, string hidden)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches();
        var client = host.CreateClient();
        await host.LoginAsync(client, WebUiTestHost.NewToken(role), roles: [role]);

        var html = await (await client.GetAsync("/")).Content.ReadHtmlAsync();

        Assert.Contains($">{visible}<", html);
        Assert.DoesNotContain($">{hidden}<", html);
    }

    [Fact]
    public async Task Anonymous_menu_shows_login_and_register_only()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches();

        var html = await (await host.CreateClient().GetAsync("/")).Content.ReadHtmlAsync();

        Assert.Contains("Giriş yap", html);
        Assert.Contains("Kayıt ol", html);
        Assert.DoesNotContain(">Yönetim<", html);
        Assert.DoesNotContain("Çıkış yap", html);
    }

    [Fact]
    public async Task Every_controller_action_declares_authorization_explicitly()
    {
        await using var host = await WebUiTestHost.StartAsync(withProbe: false);

        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(host.Factory.Services);

        Assert.Empty(missing);
    }
}

internal static class TestUrls
{
    // Yönlendirme adresi bazen göreli ("/"), bazen mutlak (kimlik doğrulama çağrısı); karşılaştırma için hep göreli.
    public static string? Relative(Uri? location)
        => location is null ? null : location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
}
