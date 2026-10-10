using System.Net;
using System.Text.RegularExpressions;
using Hastane.TestSupport;

namespace Hastane.WebUI.Tests;

// Doccure tarzı üst menü ve genel (anonim) sayfalar: Hakkımızda, İletişim, SSS, 404 sayfası.
public class SiteMenuAndPublicPagesTests
{
    // ---------- Üst menü ----------

    [Fact]
    public async Task Anonymous_menu_has_public_dropdowns_and_login_register_under_patients()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");

        var html = await (await host.CreateClient().GetAsync("/")).Content.ReadHtmlAsync();

        Assert.Contains(">Ana Sayfa<", html);
        Assert.Contains(">Doktorlar<", html);
        Assert.Contains(">Hastalar<", html);
        Assert.Contains(">Sayfalar<", html);
        Assert.Contains("href=\"/Home/About\"", html);
        Assert.Contains("href=\"/Home/Contact\"", html);
        Assert.Contains("href=\"/Home/Faq\"", html);
        Assert.Contains("href=\"/Account/Login\"", html);
        Assert.Contains("href=\"/Account/Register\"", html);
        Assert.DoesNotContain(">Yönetim<", html);
        Assert.DoesNotContain(">Doktor Paneli<", html);
        Assert.DoesNotContain(">Randevularım<", html);
        Assert.DoesNotContain("Favoriler", html);
        Assert.Contains("navbar-toggler", html);   // mobil hamburger
        Assert.Contains("id=\"mainNav\"", html);    // Bootstrap collapse hedefi
    }

    [Fact]
    public async Task Patient_menu_lists_links_including_real_favorites_messages_and_profile_links()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/")).Content.ReadHtmlAsync();

        Assert.Contains(">Hastalar<", html);
        foreach (var label in new[] { "Panel", "Randevularım", "Reçetelerim", "Ödemelerim", "Faturalarım", "Favoriler", "Profil Bilgilerim", "Profil Ayarları", "Şifre Değiştir" })
            Assert.Contains($"</i>{label}</a>", html);
        foreach (var href in new[] { "/Patient", "/Patient/Appointments", "/Patient/Prescriptions", "/Patient/Payments", "/Patient/Invoices", "/Patient/Favorites", "/Messages", "/Patient/Profile", "/Account/Profile", "/Account/ChangePassword" })
            Assert.Contains($"href=\"{href}\"", html);

        // Favoriler ve Mesajlar artık gerçek bağlantı: "yakında" rozeti ve devre dışı öğe yok.
        Assert.Matches(new Regex("<a class=\"dropdown-item\" href=\"/Messages\"><i class=\"bi bi-chat-dots\" aria-hidden=\"true\"></i><span>Mesajlar</span>"), html);
        // Razor "Mesajlar@await" yazımını e-posta sanıp kodu düz metin basıyordu; sayfada çiğ Razor kalmamalı.
        Assert.DoesNotContain("@await", html);
        Assert.DoesNotContain("yakında", html);
        Assert.DoesNotContain("dropdown-item disabled", html);

        Assert.DoesNotContain(">Yönetim<", html);
        Assert.DoesNotContain(">Doktor Paneli<", html);
    }

    [Fact]
    public async Task Doctor_menu_lists_all_ten_panel_pages_and_hides_patient_and_admin_menus()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        var client = await host.LoginAsAsync("Doctor");

        var html = await (await client.GetAsync("/")).Content.ReadHtmlAsync();

        Assert.Contains(">Doktor Paneli<", html);
        foreach (var label in new[] { "Panel", "Randevular", "Takvim", "Hastalarım", "Çalışma Saatleri", "Reçeteler", "Faturalarım", "Yorumlar", "Profil", "Sosyal Medya" })
            Assert.Contains($"</i>{label}</a>", html);
        foreach (var href in new[] { "/Doctor", "/Doctor/Appointments", "/Doctor/Calendar", "/Doctor/Patients", "/Doctor/Schedule", "/Doctor/Prescriptions", "/Doctor/Invoices", "/Doctor/Reviews", "/Doctor/Profile", "/Doctor/Social" })
            Assert.Contains($"href=\"{href}\"", html);
        Assert.DoesNotContain(">Hastalar<", html);
        Assert.DoesNotContain(">Randevularım<", html);
        Assert.DoesNotContain(">Yönetim<", html);
    }

    [Fact]
    public async Task Admin_menu_is_a_single_admin_link_without_a_submenu()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        var client = await host.LoginAsAsync("Admin");

        var html = await (await client.GetAsync("/")).Content.ReadHtmlAsync();

        Assert.Matches(new Regex("<a class=\"nav-link[^\"]*\" href=\"/Admin\"><span>Yönetim</span></a>"), html);
        Assert.DoesNotContain("href=\"/Admin/Branches\"", html);
        Assert.DoesNotContain("href=\"/Admin/Users\"", html);
        Assert.DoesNotContain(">Doktor Paneli<", html);
        Assert.DoesNotContain(">Randevularım<", html);
        Assert.DoesNotContain(">Hastalar<", html);
    }

    // ---------- Genel sayfalar ----------

    [Theory]
    [InlineData("/Home/About", "eğitim projesi")]
    [InlineData("/Home/Contact", "Mesaj gönderin")]
    [InlineData("/Home/Faq", "Randevuyu nasıl alırım?")]
    public async Task Public_pages_open_anonymously_without_calling_the_gateway(string path, string expected)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync(path);
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, html);
        Assert.Empty(host.Gateway.Requests);
    }

    [Fact]
    public async Task Faq_has_eight_to_ten_accordion_questions_including_coming_soon_password_reset()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var html = await (await host.CreateClient().GetAsync("/Home/Faq")).Content.ReadHtmlAsync();

        var count = Regex.Matches(html, "class=\"accordion-item\"").Count;
        Assert.InRange(count, 8, 10);
        Assert.Contains("data-bs-toggle=\"collapse\"", html);
        Assert.Contains("Şifremi unuttum", html);
        Assert.Contains("yakında", html);
    }

    [Fact]
    public async Task Contact_form_posts_nothing_to_the_gateway_and_shows_the_education_notice()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = host.CreateClient();
        var token = WebUiTestHost.ExtractAntiforgeryToken(await (await client.GetAsync("/Home/Contact")).Content.ReadAsStringAsync());

        var response = await client.PostAsync("/Home/Contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Ayşe Yılmaz",
            ["Email"] = "ayse@test.local",
            ["Subject"] = "Soru",
            ["Message"] = "Merhaba, bir sorum var.",
            ["__RequestVerificationToken"] = token,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Home/Contact", TestUrls.Relative(response.Headers.Location));
        var next = await (await client.GetAsync("/Home/Contact")).Content.ReadHtmlAsync();
        Assert.Contains("Mesajınız alındı (eğitim projesi, kaydedilmez).", next);
        Assert.Empty(host.Gateway.Requests); // gateway'e hiçbir şey gitmedi
    }

    [Fact]
    public async Task Contact_form_shows_validation_errors_and_requires_antiforgery()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = host.CreateClient();
        var token = WebUiTestHost.ExtractAntiforgeryToken(await (await client.GetAsync("/Home/Contact")).Content.ReadAsStringAsync());

        var invalid = await client.PostAsync("/Home/Contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "",
            ["Email"] = "gecersiz",
            ["Subject"] = "",
            ["Message"] = "",
            ["__RequestVerificationToken"] = token,
        }));
        var html = await invalid.Content.ReadHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Adınızı yazın.", html);
        Assert.Contains("Geçerli bir e-posta adresi yazın.", html);
        Assert.Contains("Mesajınızı yazın.", html);
        Assert.DoesNotContain("Mesajınız alındı", html);

        var noToken = await client.PostAsync("/Home/Contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "A",
            ["Email"] = "a@b.co",
            ["Subject"] = "S",
            ["Message"] = "M",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Empty(host.Gateway.Requests);
    }

    [Fact]
    public async Task Not_found_page_is_designed_with_illustration_and_links()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync("/boyle-bir-sayfa-yok");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Sayfa bulunamadı", html);
        Assert.Contains("/img/404.svg", html);
        Assert.Contains("href=\"/\"", html);
        Assert.Contains("href=\"/Doctors\"", html);
    }

    [Fact]
    public async Task Non_404_error_pages_keep_the_plain_code_and_home_link()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var html = await (await host.CreateClient().GetAsync("/Error/503")).Content.ReadHtmlAsync();

        Assert.Contains("Servis şu an yanıt vermiyor", html);
        Assert.DoesNotContain("/img/404.svg", html);
        Assert.Contains("Ana sayfaya dön", html);
    }

    [Fact]
    public async Task Authorization_coverage_includes_the_new_public_and_doctor_endpoints()
    {
        await using var host = await WebUiTestHost.StartAsync(withProbe: false);

        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(host.Factory.Services);

        Assert.Empty(missing);
    }
}
