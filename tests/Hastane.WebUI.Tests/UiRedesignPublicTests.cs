using System.Net;
using System.Web;
using Hastane.WebUI.ViewModels.Shared;
using static Hastane.WebUI.Tests.PublicPatientTestHelpers;

namespace Hastane.WebUI.Tests;

// Görünüm yenilemesinin herkese açık yüzü: ana sayfa, doktor kartı, avatar yedeği, çoklu branş süzgeci, yardımcılar.
public class UiKitTests
{
    [Theory]
    [InlineData("Ayşe Kaya", "AK")]
    [InlineData("Dr. Ali Veli", "AV")]
    [InlineData("Prof. Dr. Mehmet", "M")]
    [InlineData("ışıl Öz", "IÖ")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Initials_skip_titles_and_use_turkish_casing(string? name, string expected)
        => Assert.Equal(expected, UiKit.Initials(name));

    [Theory]
    [InlineData("Kardiyoloji", "cardiology")]
    [InlineData("Nöroloji", "neurology")]
    [InlineData("Diş Hekimliği", "dental")]
    [InlineData("Göz Hastalıkları", "ophthalmology")]
    [InlineData("Ortopedi ve Travmatoloji", "orthopedics")]
    [InlineData("Çocuk Sağlığı", "pediatrics")]
    [InlineData("Dermatoloji", "dermatology")]
    [InlineData("Üroloji", "urology")]
    [InlineData("Dahiliye", "general")]
    [InlineData(null, "general")]
    public void BranchIcon_matches_by_name_and_falls_back_to_general(string? branch, string expectedFile)
        => Assert.Equal($"/img/branches/{expectedFile}.svg", UiKit.BranchIcon(branch));

    [Theory]
    [InlineData("/img/doctors/avatar-01.svg", "/img/doctors/avatar-01.svg")]
    [InlineData("https://cdn.test/a.jpg", "https://cdn.test/a.jpg")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:text/html,<script>", null)]
    [InlineData("//evil.test/a.jpg", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SafeImageUrl_accepts_only_local_paths_and_http(string? url, string? expected)
        => Assert.Equal(expected, UiKit.SafeImageUrl(url));

    [Fact]
    public void AvatarColor_is_stable_for_the_same_name()
        => Assert.Equal(UiKit.AvatarColor("Ayşe Kaya"), UiKit.AvatarColor("Ayşe Kaya"));
}

public class HomePageTests
{
    [Fact]
    public async Task Home_has_hero_search_form_branch_icons_featured_doctors_and_steps()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji", "Dahiliye");
        var search = host.Gateway.MapLogged("GET", "/gateway/doctors/search", 200, Paged([SearchItem("d1", "Ayşe", "Kaya")], 1));
        host.Gateway.MapJson("GET", "/gateway/reviews/summaries", 200, new[]
        {
            new { doctorId = "d1", reviewCount = 7, averageRating = 4.5, recommendationPercent = 90 },
        });

        var response = await host.CreateClient().GetAsync("/");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Arama formu mevcut parametre adlarıyla /Doctors'a gider: q + city.
        Assert.Contains("action=\"/Doctors\"", html);
        Assert.Contains("name=\"q\"", html);
        Assert.Contains("name=\"city\"", html);
        Assert.Contains("/img/hero.svg", html);
        // Branş kartları: ada göre ikon, eşleşmeyene genel ikon.
        Assert.Contains("/img/branches/cardiology.svg", html);
        Assert.Contains("/img/branches/general.svg", html);
        Assert.Contains("href=\"/Doctors?branchId=b0\"", html);
        // Öne çıkan doktorlar: ilk 6 kayıt, puan özeti tek toplu çağrı.
        Assert.Contains("Dr. Ayşe Kaya", html);
        Assert.Contains("7 yorum", html);
        Assert.Equal("?page=1&pageSize=6", search.LastQuery);
        Assert.Contains("Nasıl çalışır", html);
    }

    [Fact]
    public async Task Home_still_opens_with_warnings_when_services_are_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/branches", 503);
        host.Gateway.MapStatus("GET", "/gateway/doctors/search", 503);

        var response = await host.CreateClient().GetAsync("/");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Branş listesi şu anda yüklenemiyor", html);
        Assert.Contains("Doktor listesi şu anda yüklenemiyor", html);
    }

    [Fact]
    public async Task Layout_menu_has_doctors_dropdown_footer_columns_and_education_note()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");

        var html = await (await host.CreateClient().GetAsync("/")).Content.ReadHtmlAsync();

        Assert.Contains("href=\"/Doctors\"", html);
        Assert.Contains("href=\"/Branches\"", html);
        Assert.Contains("data-bs-toggle=\"dropdown\"", html);
        Assert.Contains("navbar-toggler", html); // mobil hamburger
        Assert.Contains("Hızlı bağlantılar", html);
        Assert.Contains("İletişim", html);
        Assert.Contains("Eğitim projesi", html);
    }

    [Fact]
    public async Task Logged_in_user_menu_shows_avatar_with_name_and_logout()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/")).Content.ReadHtmlAsync();

        Assert.Contains("Ayşe Yılmaz", html);
        Assert.Contains("class=\"avatar avatar-sm\"", html);
        Assert.Contains("Hasta paneli", html);
        Assert.Contains("Çıkış yap", html);
    }
}

public class DoctorCardAndSearchRedesignTests
{
    [Fact]
    public async Task Card_without_photo_falls_back_to_svg_initials_avatar()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, Paged([SearchItem("d1", "Ayşe", "Kaya")], 1));

        var html = await (await host.CreateClient().GetAsync("/Doctors")).Content.ReadHtmlAsync();

        Assert.Contains("<svg class=\"avatar avatar-lg\"", html);
        Assert.Contains(">AK</text>", html);
    }

    [Fact]
    public async Task Card_with_photo_gallery_services_recommendation_and_fee_range()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, Paged(
        [
            new
            {
                id = "d1", firstName = "Ayşe", lastName = "Kaya", title = "Dr.", degrees = "Uzman", gender = "Female", branchId = "b0",
                imageUrl = "/img/doctors/avatar-01.svg",
                services = new[] { "EKG", "Efor testi" },
                gallery = new[] { "/img/clinics/clinic-01.svg", "/img/clinics/clinic-02.svg", "javascript:alert(1)" },
                primaryCity = "Ankara", minFee = 400m, maxFee = 600m, currency = "TRY",
            },
        ], 1));
        host.Gateway.MapJson("GET", "/gateway/reviews/summaries", 200, new[]
        {
            new { doctorId = "d1", reviewCount = 17, averageRating = 4.0, recommendationPercent = 98 },
        });

        var html = await (await host.CreateClient().GetAsync("/Doctors")).Content.ReadHtmlAsync();

        Assert.Contains("src=\"/img/doctors/avatar-01.svg\"", html);
        Assert.DoesNotContain("<svg class=\"avatar avatar-lg\"", html);
        Assert.Contains("src=\"/img/clinics/clinic-01.svg\"", html);
        Assert.Contains("src=\"/img/clinics/clinic-02.svg\"", html);
        Assert.DoesNotContain("javascript:", html); // güvenilmeyen şema görsel olarak basılmaz
        Assert.Contains("Efor testi", html);
        Assert.Contains("%98 tavsiye", html);
        Assert.Contains("400,00 TRY - 600,00 TRY", html);
        Assert.Contains("Profili Gör", html);
        Assert.Contains("Randevu Al", html);
        Assert.Contains("1</strong> doktor bulundu", html);
    }

    [Fact]
    public async Task Book_button_is_hidden_from_doctors_and_admins()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, Paged([SearchItem("d1", "Ayşe", "Kaya")], 1));
        var client = await host.LoginAsAsync("Doctor");

        var html = await (await client.GetAsync("/Doctors")).Content.ReadHtmlAsync();

        Assert.Contains("Profili Gör", html);
        Assert.DoesNotContain("Randevu Al", html);
    }

    [Fact]
    public async Task Filter_panel_sends_multiple_branches_as_one_comma_separated_branchIds()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji", "Dahiliye");
        var search = host.Gateway.MapLogged("GET", "/gateway/doctors/search", 200, Paged([], 0));

        var response = await host.CreateClient().GetAsync("/Doctors?branchId=b0&branchId=b1&gender=Male");
        var html = await response.Content.ReadHtmlAsync();

        var query = HttpUtility.ParseQueryString(search.LastQuery);
        Assert.Equal("b0,b1", query["branchIds"]);
        Assert.Equal("Male", query["gender"]);
        // Seçili branşlar işaretli kalır, çoklu seçim kutuları var.
        Assert.Contains("name=\"branchId\" id=\"b-b0\" value=\"b0\" checked", html);
        Assert.Contains("name=\"branchId\" id=\"b-b1\" value=\"b1\" checked", html);
        Assert.Contains("type=\"date\"", html);
        Assert.Contains("name=\"minFee\"", html);
        Assert.Contains("name=\"sort\"", html);
    }

    [Fact]
    public async Task Profile_has_tabs_timeline_booking_box_and_avatar_fallback()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        host.Gateway.MapJson("GET", "/gateway/reviews/doctors/d1/summary", 200, new { doctorId = "d1", reviewCount = 2, averageRating = 4.5, recommendationPercent = 100 });
        host.Gateway.MapJson("GET", "/gateway/reviews", 200, Array.Empty<object>());
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.Contains("Genel bakış", html);
        Assert.Contains("Konumlar", html);
        Assert.Contains("Çalışma saatleri", html);
        Assert.Contains("class=\"timeline", html);
        Assert.Contains("booking-box", html);
        Assert.Contains("<svg class=\"avatar avatar-xl\"", html); // Doctor() örneğinde fotoğraf yok
        Assert.Contains("Muayene ücreti: <strong>500,00 TRY</strong>", html);
    }
}
