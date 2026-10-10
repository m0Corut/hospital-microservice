using System.Net;
using System.Text.RegularExpressions;
using static Hastane.WebUI.Tests.AdminPanelTestHelpers;

namespace Hastane.WebUI.Tests;

// Şifremi unuttum / şifre sıfırlama / doktor başvurusu (anonim akışlar) ve kendi başvuru durumu. Sahte gateway deseni (UI-KURALLARI §7).
public class ForgotPasswordTests
{
    private const string Sent = "Bu e-posta kayıtlıysa sıfırlama bağlantısı gönderildi";

    [Fact]
    public async Task Login_page_links_to_forgot_password()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var html = await HtmlAsync(await host.CreateClient().GetAsync("/Account/Login"));

        Assert.Contains("Şifremi unuttum", html);
        Assert.Contains("href=\"/Account/ForgotPassword\"", html);
    }

    [Fact]
    public async Task Get_is_anonymous_and_shows_the_form()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync("/Account/ForgotPassword");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"Email\"", await HtmlAsync(response));
        Assert.Empty(host.Gateway.Requests);
    }

    [Theory]
    [InlineData(202)] // kayıtlı ya da değil: Identity hep 202
    [InlineData(429)] // hız sınırı: kullanıcıya fark ettirilmez
    [InlineData(404)] // beklenmeyen yanıt: yine aynı mesaj
    public async Task Same_message_whatever_the_service_answers(int status)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/auth/forgot-password", status, new { message = "x" });
        var client = host.CreateClient();

        var response = await PostAsync(host, client, "/Account/ForgotPassword", "/Account/ForgotPassword", new() { ["Email"] = "kimse@test.local" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", Location(response));
        Assert.Contains(Sent, await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Posts_only_the_email_to_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/auth/forgot-password", 202, new { message = "x" });

        await PostAsync(host, host.CreateClient(), "/Account/ForgotPassword", "/Account/ForgotPassword", new() { ["Email"] = " ayse@test.local " });

        var sent = Json(Last(host, "POST", "/gateway/auth/forgot-password"));
        Assert.Equal("ayse@test.local", sent.GetProperty("email").GetString());
        Assert.Single(sent.EnumerateObject());
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected_and_never_reaches_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().PostAsync("/Account/ForgotPassword",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "ayse@test.local" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Gateway.Requests);
    }

    [Fact]
    public async Task Malformed_email_is_a_form_error_without_a_service_call()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await PostAsync(host, host.CreateClient(), "/Account/ForgotPassword", "/Account/ForgotPassword", new() { ["Email"] = "bu-bir-eposta-degil" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Geçerli bir e-posta girin", await HtmlAsync(response));
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/auth/forgot-password");
    }

    [Fact]
    public async Task Service_format_error_400_is_shown_on_the_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/auth/forgot-password", 400, new { status = 400, title = "Hata", detail = "E-posta biçimi geçersiz." });

        var response = await PostAsync(host, host.CreateClient(), "/Account/ForgotPassword", "/Account/ForgotPassword", new() { ["Email"] = "ayse@test.local" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("E-posta biçimi geçersiz", await HtmlAsync(response));
    }
}

public class ResetPasswordTests
{
    private const string Token = "TOKEN-ABC123_xyz";
    private const string Page = "/Account/ResetPassword?email=ayse%40test.local&token=" + Token;

    private static Dictionary<string, string> Fields(string password = "Yeni1234", string confirm = "Yeni1234") => new()
    {
        ["Email"] = "ayse@test.local",
        ["Token"] = Token,
        ["NewPassword"] = password,
        ["ConfirmPassword"] = confirm,
    };

    [Fact]
    public async Task Get_carries_the_token_only_in_a_hidden_field_and_loads_nothing_external()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync(Page);
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(new Regex("<input[^>]*type=\"hidden\"[^>]*value=\"" + Token + "\"", RegexOptions.IgnoreCase), html);
        Assert.Single(Regex.Matches(html, Regex.Escape(Token))); // görünür metinde, bağlantıda, başlıkta başka yerde yok
        Assert.DoesNotMatch(new Regex("<(script|link|img|iframe)[^>]+(src|href)=\"https?://", RegexOptions.IgnoreCase), html);
        Assert.Empty(host.Gateway.Requests);
    }

    [Fact]
    public async Task Get_and_post_send_Referrer_Policy_no_referrer()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("POST", "/gateway/auth/reset-password", 400);
        var client = host.CreateClient();

        var get = await client.GetAsync(Page);
        var post = await PostAsync(host, client, Page, "/Account/ResetPassword", Fields());

        Assert.Equal(["no-referrer"], get.Headers.GetValues("Referrer-Policy"));
        Assert.Equal(["no-referrer"], post.Headers.GetValues("Referrer-Policy"));
    }

    [Fact]
    public async Task Link_without_token_says_it_is_invalid_and_offers_a_new_one()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var html = await HtmlAsync(await host.CreateClient().GetAsync("/Account/ResetPassword"));

        Assert.Contains("Bağlantı geçersiz ya da süresi dolmuş", html);
        Assert.Contains("/Account/ForgotPassword", html);
        Assert.DoesNotContain("name=\"NewPassword\"", html);
    }

    [Fact]
    public async Task Success_posts_to_the_gateway_and_redirects_to_login_with_a_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("POST", "/gateway/auth/reset-password", 204);
        var client = host.CreateClient();

        var response = await PostAsync(host, client, Page, "/Account/ResetPassword", Fields());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", Location(response));
        Assert.DoesNotContain(Token, Location(response)!); // belirteç yönlendirme adresine taşınmaz
        var sent = Json(Last(host, "POST", "/gateway/auth/reset-password"));
        Assert.Equal("ayse@test.local", sent.GetProperty("email").GetString());
        Assert.Equal(Token, sent.GetProperty("token").GetString());
        Assert.Equal("Yeni1234", sent.GetProperty("newPassword").GetString());
        Assert.Contains("Şifreniz sıfırlandı", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Invalid_or_expired_token_400_shows_the_generic_message_and_keeps_the_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/auth/reset-password", 400, new { status = 400, title = "Şifre sıfırlanamadı", detail = "ham-servis-metni" });

        var response = await PostAsync(host, host.CreateClient(), Page, "/Account/ResetPassword", Fields());
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bağlantı geçersiz ya da süresi dolmuş", html);
        Assert.DoesNotContain("ham-servis-metni", html);
        Assert.DoesNotContain("Yeni1234", html); // parola geri yazılmaz
        Assert.Single(Regex.Matches(html, Regex.Escape(Token))); // yalnız gizli alan
    }

    [Fact]
    public async Task Weak_password_400_shows_the_field_errors()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/auth/reset-password", 400, new
        {
            status = 400,
            title = "Doğrulama",
            errors = new Dictionary<string, string[]> { ["PasswordRequiresUpper"] = ["Şifre en az bir büyük harf içermeli."] },
        });

        var html = await HtmlAsync(await PostAsync(host, host.CreateClient(), Page, "/Account/ResetPassword", Fields("kucuk1234", "kucuk1234")));

        Assert.Contains("Şifre en az bir büyük harf içermeli", html);
        Assert.DoesNotContain("Bağlantı geçersiz", html);
    }

    [Fact]
    public async Task Mismatched_passwords_never_reach_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var html = await HtmlAsync(await PostAsync(host, host.CreateClient(), Page, "/Account/ResetPassword", Fields("Yeni1234", "Baska1234")));

        Assert.Contains("Şifreler aynı değil", html);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/auth/reset-password");
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().PostAsync("/Account/ResetPassword", new FormUrlEncodedContent(Fields()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Gateway.Requests);
    }
}

public class RegisterDoctorTests
{
    private static Dictionary<string, string> Fields() => new()
    {
        ["FirstName"] = "Ali",
        ["LastName"] = "Veli",
        ["Email"] = "ali@test.local",
        ["Password"] = "Sifre1234",
        ["ConfirmPassword"] = "Sifre1234",
        ["BranchId"] = "b1",
        ["LicenseNumber"] = "TR-12345",
        ["Note"] = "Merhaba",
    };

    [Fact]
    public async Task Register_page_links_to_the_doctor_application()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var html = await HtmlAsync(await host.CreateClient().GetAsync("/Account/Register"));

        Assert.Contains("Doktor musunuz?", html);
        Assert.Contains("href=\"/Account/RegisterDoctor\"", html);
    }

    [Fact]
    public async Task Form_lists_branches_from_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji", "Dahiliye");

        var response = await host.CreateClient().GetAsync("/Account/RegisterDoctor");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("<option[^>]*value=\"b0\"[^>]*>Kardiyoloji</option>", html);
        Assert.Matches("<option[^>]*value=\"b1\"[^>]*>Dahiliye</option>", html);
        Assert.Contains("name=\"LicenseNumber\"", html);
        Assert.Contains("name=\"Note\"", html);
    }

    [Fact]
    public async Task Form_opens_with_a_notice_when_the_branch_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/branches", 503);

        var response = await host.CreateClient().GetAsync("/Account/RegisterDoctor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Branş listesi şu an yüklenemedi", await HtmlAsync(response));
    }

    [Fact]
    public async Task Success_posts_the_application_body_and_redirects_to_login_with_a_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji", "Dahiliye");
        host.Gateway.MapJson("POST", "/gateway/auth/register-doctor", 201, new { id = Guid.NewGuid(), status = "Pending" });
        var client = host.CreateClient();

        var response = await PostAsync(host, client, "/Account/RegisterDoctor", "/Account/RegisterDoctor", Fields());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", Location(response));
        var sent = Json(Last(host, "POST", "/gateway/auth/register-doctor"));
        Assert.Equal("Ali", sent.GetProperty("firstName").GetString());
        Assert.Equal("Veli", sent.GetProperty("lastName").GetString());
        Assert.Equal("ali@test.local", sent.GetProperty("email").GetString());
        Assert.Equal("Sifre1234", sent.GetProperty("password").GetString());
        Assert.Equal("b1", sent.GetProperty("branchId").GetString());
        Assert.Equal("TR-12345", sent.GetProperty("licenseNumber").GetString());
        Assert.Equal("Merhaba", sent.GetProperty("note").GetString());
        Assert.False(sent.TryGetProperty("role", out _)); // başvuran rolünü kendisi seçemez
        Assert.Contains("Başvurunuz alındı; onaylanınca doktor paneliniz açılır", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Email_conflict_409_is_shown_on_the_email_field_and_the_form_keeps_its_branches()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji", "Dahiliye");
        host.MapProblem("POST", "/gateway/auth/register-doctor", 409, "Bu e-posta adresi zaten kayıtlı.");

        var response = await PostAsync(host, host.CreateClient(), "/Account/RegisterDoctor", "/Account/RegisterDoctor", Fields());
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bu e-posta adresi zaten kayıtlı", html);
        Assert.Matches("<option[^>]*selected[^>]*value=\"b1\"[^>]*>Dahiliye</option>|<option[^>]*value=\"b1\"[^>]*selected[^>]*>Dahiliye</option>", html);
        Assert.DoesNotContain("Sifre1234", html);
    }

    [Fact]
    public async Task Missing_branch_or_license_is_a_form_error_without_a_service_call()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        var fields = Fields();
        fields["BranchId"] = "";
        fields["LicenseNumber"] = "";

        var html = await HtmlAsync(await PostAsync(host, host.CreateClient(), "/Account/RegisterDoctor", "/Account/RegisterDoctor", fields));

        Assert.Contains("Branş seçin", html);
        Assert.Contains("Lisans numarası gerekli", html);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/auth/register-doctor");
    }

    [Fact]
    public async Task Service_validation_400_is_shown_on_the_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("POST", "/gateway/auth/register-doctor", 400, new
        {
            status = 400,
            title = "Doğrulama",
            errors = new Dictionary<string, string[]> { ["PasswordRequiresDigit"] = ["Şifre en az bir rakam içermeli."] },
        });

        var html = await HtmlAsync(await PostAsync(host, host.CreateClient(), "/Account/RegisterDoctor", "/Account/RegisterDoctor", Fields()));

        Assert.Contains("Şifre en az bir rakam içermeli", html);
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().PostAsync("/Account/RegisterDoctor", new FormUrlEncodedContent(Fields()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/auth/register-doctor");
    }
}

public class DoctorApplicationStatusTests
{
    private static object Application(string status, string? decisionNote = null) => new
    {
        id = Guid.NewGuid(),
        userId = "user-1",
        firstName = "Ali",
        lastName = "Veli",
        email = "ali@test.local",
        branchId = "b0",
        licenseNumber = "TR-12345",
        note = "Merhaba",
        status,
        createdAt = new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc),
        decidedAt = status == "Pending" ? (DateTime?)null : new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc),
        decisionNote,
    };

    [Fact]
    public async Task Anonymous_is_redirected_to_login()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync("/Account/DoctorApplication");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", Location(response));
    }

    [Fact]
    public async Task Pending_application_shows_waiting_status_and_details()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/auth/me/doctor-application", 200, Application("Pending"));
        var client = await ClientAsync(host, "Patient");

        var html = await HtmlAsync(await client.GetAsync("/Account/DoctorApplication"));

        Assert.Contains("Bekliyor", html);
        Assert.Contains("Kardiyoloji", html);
        Assert.Contains("TR-12345", html);
        Assert.StartsWith("Bearer ", Last(host, "GET", "/gateway/auth/me/doctor-application").Authorization);
    }

    [Fact]
    public async Task Approved_application_tells_the_user_to_sign_in_again()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me/doctor-application", 200, Application("Approved"));
        var client = await ClientAsync(host, "Patient");

        var html = await HtmlAsync(await client.GetAsync("/Account/DoctorApplication"));

        Assert.Contains("Onaylandı", html);
        Assert.Contains("Yeniden giriş yapın (yetkileriniz yenilensin)", html);
    }

    [Fact]
    public async Task Rejected_application_shows_the_decision_note()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me/doctor-application", 200, Application("Rejected", "Lisans doğrulanamadı."));
        var client = await ClientAsync(host, "Patient");

        var html = await HtmlAsync(await client.GetAsync("/Account/DoctorApplication"));

        Assert.Contains("Reddedildi", html);
        Assert.Contains("Lisans doğrulanamadı.", html);
        Assert.DoesNotContain("Yeniden giriş yapın (yetkileriniz yenilensin)", html);
    }

    [Fact]
    public async Task No_application_404_shows_an_info_text_without_an_apply_link()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/auth/me/doctor-application", 404);
        var client = await ClientAsync(host, "Patient");

        var response = await client.GetAsync("/Account/DoctorApplication");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Başvurunuz yok", html);
        Assert.DoesNotContain("href=\"/Account/RegisterDoctor\"", html);
    }
}
