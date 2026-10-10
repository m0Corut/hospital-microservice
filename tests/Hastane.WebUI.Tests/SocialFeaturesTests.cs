using System.Net;
using System.Text.RegularExpressions;
using Hastane.TestSupport;
using static Hastane.WebUI.Tests.PublicPatientTestHelpers;

namespace Hastane.WebUI.Tests;

internal static class SocialTestHelpers
{
    // Menü "kabuğu" çağrıları (zil, mesaj rozeti) her girişli sayfa çiziminde gider; "servise hiç gidilmez" iddialarını
    // etkilemesin diye bu sayaç yalnız alan çağrılarını sayar. Kabuk çağrıları ayrı testlerde (Bell_*, Message_badge_*) sınanır.
    public static int ApiCallCount(this FakeGateway gateway)
        => gateway.Requests.Count(r => !r.Path.StartsWith("/gateway/notifications/me", StringComparison.Ordinal)
                                       && r.Path != "/gateway/chat/unread");

    public static object Notification(string id, string title, string? link, bool read = false) => new
    {
        id,
        title,
        message = title + " ayrıntısı",
        link,
        kind = "Info",
        createdAt = DateTime.UtcNow.AddHours(-2),
        readAt = read ? DateTime.UtcNow.AddHours(-1) : (DateTime?)null,
    };

    /// <summary>GET notifications/me: pageSize=1 sorgusu da aynı sayfa gövdesini (unreadCount ile) alır.</summary>
    public static CallLog MapNotifications(this FakeGateway gateway, long unread, params object[] items)
    {
        var log = new CallLog();
        gateway.Map("GET", "/gateway/notifications/me", ctx =>
        {
            log.Queries.Enqueue(ctx.Request.QueryString.Value ?? string.Empty);
            var pageSize = int.TryParse(ctx.Request.Query["pageSize"], out var ps) ? ps : 20;
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsJsonAsync(new { items = items.Take(pageSize), totalCount = items.Length, unreadCount = unread, page = 1, pageSize });
        });
        return log;
    }

    public static object Conversation(string id = "c1", int unread = 2) => new
    {
        id,
        patientUserId = "user-1",
        patientName = "Ayşe Yılmaz",
        doctorId = "d1",
        doctorName = "Dr. Ali Veli",
        createdAt = DateTime.UtcNow.AddDays(-1),
        lastMessageAt = DateTime.UtcNow.AddMinutes(-5),
        lastMessagePreview = "Merhaba doktor bey",
        unreadCount = unread,
    };

    public static object ChatMessage(string id, string senderRole, string text, DateTime at) => new
    {
        id,
        senderRole,
        senderName = senderRole == "Patient" ? "Ayşe Yılmaz" : "Dr. Ali Veli",
        text,
        sentAt = at,
    };

    public static void MapConversation(this FakeGateway gateway, string id, params object[] messages)
    {
        gateway.MapJson("GET", "/gateway/chat/conversations", 200, new[] { Conversation(id) });
        gateway.MapJson("GET", $"/gateway/chat/conversations/{id}/messages", 200, messages);
        gateway.MapStatus("POST", $"/gateway/chat/conversations/{id}/read", 204);
    }
}

// ---------- Tam hasta profili: /Patient/Profile (GET/PUT /gateway/patients/me) ----------
public class PatientProfileTests
{
    private static object Profile(string? city = "Ankara") => new
    {
        userId = "user-1",
        dateOfBirth = "1990-05-17",
        age = 34,
        gender = "Female",
        bloodType = "ABNeg",
        bloodTypeDisplay = "AB Rh-",
        phone = "+90 555 111 22 33",
        address = "Atatürk Bulvarı 1",
        city,
        emergencyContactName = "Mehmet Yılmaz",
        emergencyContactPhone = "0555 000 11 22",
        updatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task Profile_page_shows_saved_values_with_readonly_age_and_blood_group()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/patients/me", 200, Profile());
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Patient/Profile");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(new Regex("id=\"age\"[^>]*value=\"34\"[^>]*readonly"), html);
        Assert.Matches(new Regex("<option value=\"ABNeg\" selected"), html);
        Assert.Contains("AB Rh-", html);
        Assert.Contains("value=\"1990-05-17\"", html);
        Assert.Contains("Ankara", html);
        Assert.Contains("Mehmet Yılmaz", html);
        Assert.Contains("href=\"/Patient/Profile\"", html); // kenar menü: Profil Bilgilerim
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/patients/me").Last().Authorization);
    }

    [Fact]
    public async Task Profile_post_puts_the_whole_profile_and_sends_no_identity_or_age()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/patients/me", 200, Profile());
        host.Gateway.MapJson("PUT", "/gateway/patients/me", 200, Profile());
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostFormAsync(client, "/Patient/Profile", new Dictionary<string, string>
        {
            ["DateOfBirth"] = "1990-05-17",
            ["Gender"] = "Female",
            ["BloodType"] = "ABNeg",
            ["Phone"] = "0555 111 22 33",
            ["Address"] = "",
            ["City"] = "İzmir",
            ["EmergencyContactName"] = "Mehmet",
            ["EmergencyContactPhone"] = "0555 000 11 22",
            ["Age"] = "99",
            ["UserId"] = "baska-biri",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Patient/Profile", Location(response));
        var put = host.Gateway.Calls("PUT", "/gateway/patients/me").Single();
        Assert.StartsWith("Bearer ", put.Authorization);
        var body = Json(put.Body);
        Assert.Equal("1990-05-17", body.GetProperty("dateOfBirth").GetString());
        Assert.Equal("Female", body.GetProperty("gender").GetString());
        Assert.Equal("ABNeg", body.GetProperty("bloodType").GetString());
        Assert.Equal("İzmir", body.GetProperty("city").GetString());
        Assert.Equal("0555 000 11 22", body.GetProperty("emergencyContactPhone").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("address").ValueKind); // PUT = tam değiştirme: boş alan null gider
        Assert.False(body.TryGetProperty("age", out _));
        Assert.False(body.TryGetProperty("userId", out _));
    }

    [Fact]
    public async Task Profile_post_carries_api_400_field_errors_back_into_the_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/patients/me", 200, Profile());
        host.Gateway.MapJson("PUT", "/gateway/patients/me", 400, Problem(400, "Geçersiz", new { DateOfBirth = new[] { "Doğum tarihi 1900'den sonra olmalı." } }));
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostFormAsync(client, "/Patient/Profile", new Dictionary<string, string> { ["DateOfBirth"] = "1990-05-17", ["City"] = "Ankara" });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // yönlendirme yok: form hatayla yeniden çizildi
        Assert.Contains("Doğum tarihi 1900'den sonra olmalı.", html);
        Assert.Contains("value=\"Ankara\"", html); // girilen değer korunur
    }

    [Fact]
    public async Task Profile_post_rejects_an_invalid_phone_locally_without_calling_the_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/patients/me", 200, Profile());
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostFormAsync(client, "/Patient/Profile", new Dictionary<string, string> { ["Phone"] = "abc" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Telefon yalnız rakam", await response.Content.ReadHtmlAsync());
        Assert.Empty(host.Gateway.Calls("PUT", "/gateway/patients/me"));
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    public async Task Profile_page_is_forbidden_for_other_roles_and_redirects_anonymous(string role)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var other = await host.LoginAsAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/Patient/Profile")).StatusCode);
        var anonymous = await host.CreateClient().GetAsync("/Patient/Profile");

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/patients/me");
    }

    [Fact]
    public async Task Patient_home_shows_age_blood_group_and_city_on_the_profile_card()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", "/gateway/patients/me", 200, Profile());
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Patient")).Content.ReadHtmlAsync();

        Assert.Matches(new Regex("id=\"profile-age\">34<"), html);
        Assert.Matches(new Regex("id=\"profile-blood\">AB Rh-<"), html);
        Assert.Matches(new Regex("id=\"profile-city\">Ankara<"), html);
    }

    [Fact]
    public async Task Patient_home_still_opens_when_the_patient_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        host.Gateway.MapStatus("GET", "/gateway/patients/me", 503);
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Patient");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Profil bilgileriniz şu anda yüklenemiyor", await response.Content.ReadHtmlAsync());
    }
}

// ---------- Favoriler ----------
public class FavoritesTests
{
    private static readonly object[] ThreeDoctors =
    [
        SearchItem("d1", "Ali", "Veli"), SearchItem("d2", "Ayşe", "Kaya"), SearchItem("d3", "Can", "Demir"),
    ];

    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync(string role = "Patient", string[]? favorites = null)
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, Paged(ThreeDoctors, 3));
        host.Gateway.MapJson("GET", "/gateway/patients/me/favorites", 200,
            (favorites ?? ["d1"]).Select(id => new { doctorId = id, createdAt = DateTime.UtcNow }));
        return (host, await host.LoginAsAsync(role));
    }

    [Fact]
    public async Task Search_page_asks_for_favorites_once_for_all_cards_and_draws_filled_and_empty_hearts()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await client.GetAsync("/Doctors");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(host.Gateway.Calls("GET", "/gateway/patients/me/favorites")); // 3 kart, TEK çağrı (N+1 yok)
        Assert.Single(Regex.Matches(html, "action=\"/Patient/Favorites/Remove\"").Cast<Match>());  // d1 favori
        Assert.Equal(2, Regex.Matches(html, "action=\"/Patient/Favorites/Add\"").Count);
        Assert.Contains("bi-heart-fill", html);
        // POST formları antiforgery belirteci taşır (form etiketi yardımcısı).
        Assert.True(Regex.Matches(html, "name=\"__RequestVerificationToken\"").Count >= 3);
        Assert.Contains("name=\"returnUrl\" value=\"/Doctors\"", html);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    public async Task Hearts_are_not_drawn_for_non_patients_and_favorites_are_not_requested(string role)
    {
        var (host, client) = await StartAsync(role);
        await using var _h = host;

        var html = await (await client.GetAsync("/Doctors")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("/Patient/Favorites/Add", html);
        Assert.DoesNotContain("/Patient/Favorites/Remove", html);
        Assert.DoesNotContain("Favorilere ekle", html);
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/patients/me/favorites"));
    }

    [Fact]
    public async Task Hearts_are_not_drawn_for_anonymous_visitors()
    {
        var (host, _) = await StartAsync();
        await using var _h = host;

        var html = await (await host.CreateClient().GetAsync("/Doctors")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("/Patient/Favorites/", html);
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/patients/me/favorites"));
    }

    [Fact]
    public async Task Doctor_profile_shows_the_heart_with_one_favorites_call()
    {
        var (host, client) = await StartAsync(favorites: ["d1"]);
        await using var _h = host;
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());

        var html = await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.Single(host.Gateway.Calls("GET", "/gateway/patients/me/favorites"));
        Assert.Contains("action=\"/Patient/Favorites/Remove\"", html);
        Assert.Contains("name=\"returnUrl\" value=\"/Doctors/d1\"", html);
    }

    [Fact]
    public async Task Heart_post_adds_with_PUT_and_returns_to_the_local_page()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("PUT", "/gateway/patients/me/favorites/d2", 204);

        var response = await host.PostAsync(client, "/Patient/Favorites/Add", new Dictionary<string, string> { ["doctorId"] = "d2", ["returnUrl"] = "/Doctors?page=2" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Doctors?page=2", Location(response));
        var put = host.Gateway.Calls("PUT", "/gateway/patients/me/favorites/d2").Single();
        Assert.StartsWith("Bearer ", put.Authorization);
        Assert.Empty(host.Gateway.Calls("DELETE", "/gateway/patients/me/favorites/d2"));
    }

    [Fact]
    public async Task Heart_post_removes_with_DELETE()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("DELETE", "/gateway/patients/me/favorites/d1", 204);

        var response = await host.PostAsync(client, "/Patient/Favorites/Remove", new Dictionary<string, string> { ["doctorId"] = "d1", ["returnUrl"] = "/Doctors" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Single(host.Gateway.Calls("DELETE", "/gateway/patients/me/favorites/d1"));
        Assert.Empty(host.Gateway.Calls("PUT", "/gateway/patients/me/favorites/d1"));
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    public async Task Heart_post_ignores_an_external_return_url(string returnUrl)
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("PUT", "/gateway/patients/me/favorites/d2", 204);

        var response = await host.PostAsync(client, "/Patient/Favorites/Add", new Dictionary<string, string> { ["doctorId"] = "d2", ["returnUrl"] = returnUrl });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", Location(response));
    }

    [Fact]
    public async Task Heart_post_without_antiforgery_token_is_rejected_and_calls_nothing()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("PUT", "/gateway/patients/me/favorites/d2", 204);

        var response = await client.PostAsync("/Patient/Favorites/Add", new FormUrlEncodedContent(new Dictionary<string, string> { ["doctorId"] = "d2" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("PUT", "/gateway/patients/me/favorites/d2"));
    }

    [Fact]
    public async Task Heart_post_is_forbidden_for_a_doctor()
    {
        var (host, client) = await StartAsync("Doctor");
        await using var _h = host;

        var response = await host.PostAsync(client, "/Patient/Favorites/Add", new Dictionary<string, string> { ["doctorId"] = "d2" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "PUT");
    }

    [Fact]
    public async Task Favorites_page_uses_one_batch_call_for_doctors_and_one_for_ratings()
    {
        var (host, client) = await StartAsync(favorites: ["d1", "d2"]);
        await using var _h = host;
        var batch = host.Gateway.MapLogged("GET", "/gateway/doctors/batch", 200, new[]
        {
            DoctorSummary("d1"),
            new { id = "d2", firstName = "Ayşe", lastName = "Kaya", title = (string?)null, degrees = "Uzman", branchId = "b0" },
        });
        var ratings = host.Gateway.MapLogged("GET", "/gateway/reviews/summaries", 200, new[]
        {
            new { doctorId = "d1", reviewCount = 3, averageRating = 4.5, recommendationPercent = 90 },
        });

        var response = await client.GetAsync("/Patient/Favorites");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, batch.Count);
        Assert.Equal(1, ratings.Count);
        Assert.Single(host.Gateway.Calls("GET", "/gateway/patients/me/favorites"));
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("Ayşe Kaya", html);
        Assert.Equal(2, Regex.Matches(html, "action=\"/Patient/Favorites/Remove\"").Count);
        Assert.Contains("(3 yorum)", html);
    }

    [Fact]
    public async Task Favorites_page_shows_empty_state_without_calling_doctor_or_review_services()
    {
        var (host, client) = await StartAsync(favorites: []);
        await using var _h = host;

        var html = await (await client.GetAsync("/Patient/Favorites")).Content.ReadHtmlAsync();

        Assert.Contains("Henüz favori doktorunuz yok", html);
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/doctors/batch"));
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/reviews/summaries"));
    }

    [Fact]
    public async Task Favorites_page_is_forbidden_for_a_doctor_and_redirects_anonymous()
    {
        var (host, doctor) = await StartAsync("Doctor");
        await using var _h = host;

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/Patient/Favorites")).StatusCode);
        var anonymous = await host.CreateClient().GetAsync("/Patient/Favorites");

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));
    }
}

// ---------- Bildirimler: zil + /Notifications ----------
public class NotificationTests
{
    [Theory]
    [InlineData("Patient")]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    public async Task Bell_shows_the_unread_count_for_every_signed_in_role(string role)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var log = host.Gateway.MapNotifications(unread: 7);
        var client = await host.LoginAsAsync(role);

        var html = await (await client.GetAsync("/Home/About")).Content.ReadHtmlAsync();

        Assert.Matches(new Regex("href=\"/Notifications\"[^>]*>.*?notif-count\">7<", RegexOptions.Singleline), html);
        Assert.Equal(1, log.Count); // sayı istek başına tek kez sorulur
        Assert.Contains("unreadOnly=true", log.LastQuery);
        Assert.Contains("pageSize=1", log.LastQuery);
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/notifications/me").Last().Authorization);
    }

    [Fact]
    public async Task Bell_has_no_badge_when_nothing_is_unread_and_caps_at_99()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        host.Gateway.MapNotifications(unread: 0);
        var none = await (await client.GetAsync("/Home/About")).Content.ReadHtmlAsync();
        host.Gateway.MapNotifications(unread: 150);
        var many = await (await client.GetAsync("/Home/About")).Content.ReadHtmlAsync();

        Assert.Contains("href=\"/Notifications\"", none);
        Assert.DoesNotContain("notif-count", none);
        Assert.Contains("notif-count\">99+<", many);
    }

    [Theory]
    [InlineData(503)]
    [InlineData(404)]
    [InlineData(401)]
    public async Task Bell_badge_is_hidden_and_page_still_opens_when_the_service_fails(int status)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/notifications/me", status);
        host.Gateway.MapStatus("GET", "/gateway/chat/unread", status);
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Home/About");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("href=\"/Notifications\"", html);
        Assert.DoesNotContain("notif-count", html);
        Assert.DoesNotContain("msg-count", html);
    }

    [Fact]
    public async Task Bell_is_not_drawn_for_anonymous_and_the_gateway_is_not_called()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var html = await (await host.CreateClient().GetAsync("/Home/About")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("href=\"/Notifications\"", html);
        Assert.Empty(host.Gateway.Requests);
    }

    [Fact]
    public async Task Notifications_page_lists_paged_items_with_unread_highlighted()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var log = host.Gateway.MapNotifications(unread: 1,
            SocialTestHelpers.Notification("n1", "Randevunuz onaylandı", "/Patient/Appointments"),
            SocialTestHelpers.Notification("n2", "Reçeteniz hazır", null, read: true));
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Notifications?page=1");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Randevunuz onaylandı", html);
        Assert.Contains("Reçeteniz hazır", html);
        Assert.Contains("bg-primary-subtle", html);                   // okunmamış vurgulu
        Assert.Single(Regex.Matches(html, "bg-primary-subtle").Cast<Match>());       // yalnız okunmamış
        Assert.Contains("Tümünü okundu yap", html);
        Assert.Single(Regex.Matches(html, ">Okundu yap<").Cast<Match>());            // okunmuşta düğme yok
        Assert.Contains(log.Queries, q => q.Contains("page=1") && q.Contains("pageSize=20") && q.Contains("unreadOnly=false"));
    }

    [Fact]
    public async Task Notifications_page_shows_only_local_links_and_never_external_ones()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapNotifications(unread: 5,
            SocialTestHelpers.Notification("n1", "yerel", "/Patient/Appointments"),
            SocialTestHelpers.Notification("n2", "dis1", "https://evil.example/phish"),
            SocialTestHelpers.Notification("n3", "dis2", "//evil.example/phish"),
            SocialTestHelpers.Notification("n4", "dis3", "javascript:alert(1)"),
            SocialTestHelpers.Notification("n5", "dis4", "/\\evil.example"));
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Notifications")).Content.ReadHtmlAsync();

        Assert.Contains("value=\"/Patient/Appointments\"", html);
        Assert.DoesNotContain("evil.example", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.Single(Regex.Matches(html, ">Aç<").Cast<Match>()); // yalnız yerel bağlantılı bildirimde "Aç" var
    }

    [Fact]
    public async Task Notifications_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapNotifications(unread: 0);
        var client = await host.LoginAsAsync("Doctor");

        var html = await (await client.GetAsync("/Notifications")).Content.ReadHtmlAsync();

        Assert.Contains("Henüz bildiriminiz yok", html);
        Assert.DoesNotContain("Tümünü okundu yap", html);
    }

    [Fact]
    public async Task Mark_read_posts_to_the_gateway_and_returns_to_the_list()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapNotifications(unread: 1, SocialTestHelpers.Notification("n1", "x", null));
        host.Gateway.MapStatus("POST", "/gateway/notifications/n1/read", 204);
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Notifications/n1/Read", new Dictionary<string, string> { ["page"] = "2" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Notifications?page=2", Location(response));
        Assert.StartsWith("Bearer ", host.Gateway.Calls("POST", "/gateway/notifications/n1/read").Single().Authorization);
    }

    [Fact]
    public async Task Mark_read_of_someone_elses_notification_is_a_quiet_404_not_an_error_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("POST", "/gateway/notifications/x/read", 404);
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Notifications/x/Read");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Mark_all_read_posts_read_all()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("POST", "/gateway/notifications/read-all", 204);
        var client = await host.LoginAsAsync("Admin");

        var response = await host.PostAsync(client, "/Notifications/ReadAll");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Single(host.Gateway.Calls("POST", "/gateway/notifications/read-all"));
    }

    [Fact]
    public async Task Open_marks_read_first_then_follows_a_local_link()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("POST", "/gateway/notifications/n1/read", 204);
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Notifications/n1/Open", new Dictionary<string, string> { ["link"] = "/Patient/Appointments" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Patient/Appointments", Location(response));
        Assert.Single(host.Gateway.Calls("POST", "/gateway/notifications/n1/read")); // önce okundu yapıldı
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("//evil.example/x")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/\\evil.example")]
    public async Task Open_never_redirects_to_an_external_link(string link)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("POST", "/gateway/notifications/n1/read", 204);
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Notifications/n1/Open", new Dictionary<string, string> { ["link"] = link });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Notifications", Location(response));
    }

    [Fact]
    public async Task Notifications_page_requires_login_and_post_requires_antiforgery()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var anonymous = await host.CreateClient().GetAsync("/Notifications");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));

        host.Gateway.MapStatus("POST", "/gateway/notifications/n1/read", 204);
        var client = await host.LoginAsAsync("Patient");
        var noToken = await client.PostAsync("/Notifications/n1/Read", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/notifications/n1/read"));
    }
}

// ---------- Mesajlar: /Messages + rozet + yoklama ucu ----------
public class MessagesTests
{
    private static readonly DateTime T0 = new(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Index_lists_conversations_with_other_party_name_preview_time_and_unread_badge()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/chat/conversations", 200, new[] { SocialTestHelpers.Conversation("c1", unread: 3) });
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Messages");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Dr. Ali Veli", html);              // hasta için karşı taraf = doktor
        Assert.DoesNotContain("class=\"ci-name\">Ayşe Yılmaz<", html); // kendi adı konuşma satırında yok
        Assert.Contains("Merhaba doktor bey", html);
        Assert.Matches(new Regex("badge rounded-pill text-bg-danger\">3<"), html);
        Assert.Contains("href=\"/Messages/c1\"", html);
        Assert.Contains("Bir konuşma seçin", html);
    }

    [Fact]
    public async Task Doctor_sees_the_patient_name_as_the_other_party()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/chat/conversations", 200, new[] { SocialTestHelpers.Conversation("c1") });
        var client = await host.LoginAsAsync("Doctor");

        var html = await (await client.GetAsync("/Messages")).Content.ReadHtmlAsync();

        Assert.Contains("class=\"ci-name\">Ayşe Yılmaz<", html);
        Assert.DoesNotContain("class=\"ci-name\">Dr. Ali Veli<", html);
    }

    [Fact]
    public async Task Conversation_shows_bubbles_in_istanbul_time_marks_mine_and_marks_the_conversation_read()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapConversation("c1",
            SocialTestHelpers.ChatMessage("m1", "Doctor", "Merhaba, nasıl yardımcı olabilirim?", T0),
            SocialTestHelpers.ChatMessage("m2", "Patient", "Sonuçlarım hazır mı?", T0.AddMinutes(5)));
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Messages/c1");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("10.10.2026 12:30", html);  // 09:30 UTC = İstanbul 12:30
        Assert.Contains("10.10.2026 12:35", html);
        Assert.Matches(new Regex("msg msg-theirs\" data-id=\"m1\""), html);
        Assert.Matches(new Regex("msg msg-mine\" data-id=\"m2\""), html);
        Assert.Single(host.Gateway.Calls("POST", "/gateway/chat/conversations/c1/read"));
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/chat/conversations/c1/messages").Single().Authorization);
        Assert.Contains("data-last=\"2026-10-10T09:35:00.000Z\"", html); // yoklama başlangıcı: son mesajın zamanı
        Assert.Contains("/Messages/c1/Since", html);
        Assert.Contains("setInterval(poll, 5000)", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Message_text_is_html_encoded_so_a_script_tag_cannot_run()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapConversation("c1", SocialTestHelpers.ChatMessage("m1", "Doctor", "<script>alert(1)</script>", T0));
        var client = await host.LoginAsAsync("Patient");

        var raw = await (await client.GetAsync("/Messages/c1")).Content.ReadAsStringAsync();

        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", raw);
        Assert.DoesNotContain("<script>alert(1)", raw);
    }

    [Fact]
    public async Task Conversation_that_is_not_in_my_list_is_404_without_reading_messages()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/chat/conversations", 200, new[] { SocialTestHelpers.Conversation("c1") });
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Messages/baskasinin");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.Contains("/messages") || r.Path.EndsWith("/read"));
    }

    [Fact]
    public async Task Send_posts_the_trimmed_text_and_redirects_back_to_the_conversation()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapConversation("c1");
        host.Gateway.MapJson("POST", "/gateway/chat/conversations/c1/messages", 201, SocialTestHelpers.ChatMessage("m9", "Patient", "Merhaba", T0));
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Messages/c1/Send", new Dictionary<string, string> { ["Form.Text"] = "  Merhaba  " });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Messages/c1", Location(response));
        var sent = host.Gateway.Calls("POST", "/gateway/chat/conversations/c1/messages").Single();
        Assert.StartsWith("Bearer ", sent.Authorization);
        Assert.Equal("Merhaba", Json(sent.Body).GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Send_rejects_an_empty_message_without_calling_the_api_and_keeps_the_page(string text)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapConversation("c1");
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Messages/c1/Send", new Dictionary<string, string> { ["Form.Text"] = text });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Mesaj boş olamaz", await response.Content.ReadHtmlAsync());
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/chat/conversations/c1/messages"));
    }

    [Fact]
    public async Task Send_rejects_a_message_over_2000_characters_and_keeps_the_text()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapConversation("c1");
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Messages/c1/Send", new Dictionary<string, string> { ["Form.Text"] = new string('a', 2001) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("1 ile 2000 karakter", await response.Content.ReadHtmlAsync());
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/chat/conversations/c1/messages"));
    }

    [Fact]
    public async Task Send_without_antiforgery_token_is_rejected()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var response = await client.PostAsync("/Messages/c1/Send", new FormUrlEncodedContent(new Dictionary<string, string> { ["Form.Text"] = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/chat/conversations/c1/messages"));
    }

    [Fact]
    public async Task Patient_starting_a_conversation_posts_the_doctor_id_and_lands_in_the_conversation()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/chat/conversations", 201, SocialTestHelpers.Conversation("c9"));
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Messages/WithDoctor/d1", new Dictionary<string, string> { ["returnUrl"] = "/Doctors/d1" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Messages/c9", Location(response));
        Assert.Equal("d1", Json(host.Gateway.Calls("POST", "/gateway/chat/conversations").Single().Body).GetProperty("doctorId").GetString());
    }

    [Fact]
    public async Task Patient_without_an_appointment_gets_the_403_message_on_the_page_they_came_from()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/chat/conversations", 403, Problem(403, "Yalnız randevunuz olan doktora yazabilirsiniz."));
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Messages/WithDoctor/d1", new Dictionary<string, string> { ["returnUrl"] = "/Branches" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Branches", Location(response));
        var next = await (await client.GetAsync("/Branches")).Content.ReadHtmlAsync();
        Assert.Contains("Yalnız randevunuz olan doktora yazabilirsiniz", next);
    }

    [Fact]
    public async Task Doctor_starting_with_a_patient_uses_the_with_patient_endpoint()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/chat/conversations/with-patient", 200, SocialTestHelpers.Conversation("c7"));
        var client = await host.LoginAsAsync("Doctor");

        var response = await host.PostAsync(client, "/Messages/WithPatient/p-42", new Dictionary<string, string> { ["returnUrl"] = "/Doctor/Patients" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Messages/c7", Location(response));
        Assert.Equal("p-42", Json(host.Gateway.Calls("POST", "/gateway/chat/conversations/with-patient").Single().Body).GetProperty("patientUserId").GetString());
    }

    [Fact]
    public async Task Start_endpoints_are_role_specific()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var doctor = await host.LoginAsAsync("Doctor");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(doctor, "/Messages/WithDoctor/d1")).StatusCode);

        var patient = await host.LoginAsAsync("Patient");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(patient, "/Messages/WithPatient/p1")).StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "POST" && r.Path.StartsWith("/gateway/chat/conversations"));
    }

    [Fact]
    public async Task Since_returns_new_messages_as_json_with_encoded_text_and_forwards_the_after_parameter()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var log = host.Gateway.MapLogged("GET", "/gateway/chat/conversations/c1/messages", 200, new[]
        {
            SocialTestHelpers.ChatMessage("m5", "Doctor", "<script>alert(1)</script>", T0.AddMinutes(10)),
        });
        host.Gateway.MapStatus("POST", "/gateway/chat/conversations/c1/read", 204);
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Messages/c1/Since?after=2026-10-10T09:30:00.000Z");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("application/json", response.Content.Headers.ContentType?.ToString());
        Assert.Contains("after=2026-10-10T09%3A30%3A00.000Z", log.LastQuery);
        var message = Json(raw).GetProperty("messages")[0];
        Assert.Equal("m5", message.GetProperty("id").GetString());
        Assert.False(message.GetProperty("mine").GetBoolean());      // doktorun mesajı, ben hastayım
        Assert.Equal("10.10.2026 12:40", message.GetProperty("time").GetString());
        Assert.Equal("<script>alert(1)</script>", message.GetProperty("text").GetString());
        Assert.DoesNotContain("<script>", raw);                      // JSON'da "<" kaçışlı (<); JS textContent ile yazar
        Assert.Single(host.Gateway.Calls("POST", "/gateway/chat/conversations/c1/read")); // karşı taraftan yeni mesaj = okundu
    }

    [Fact]
    public async Task Since_does_not_mark_read_when_only_my_own_messages_are_new()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/chat/conversations/c1/messages", 200, new[] { SocialTestHelpers.ChatMessage("m6", "Patient", "ben", T0) });
        host.Gateway.MapStatus("POST", "/gateway/chat/conversations/c1/read", 204);
        var client = await host.LoginAsAsync("Patient");

        var raw = await (await client.GetAsync("/Messages/c1/Since?after=2026-10-10T09:00:00Z")).Content.ReadAsStringAsync();

        Assert.True(Json(raw).GetProperty("messages")[0].GetProperty("mine").GetBoolean());
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/chat/conversations/c1/read"));
    }

    [Fact]
    public async Task Since_is_404_when_the_gateway_says_i_am_not_a_party_to_the_conversation()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/chat/conversations/baskasinin/messages", 404);
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Messages/baskasinin/Since?after=2026-10-10T09:00:00Z");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?after=bozuk")]
    public async Task Since_requires_a_valid_after_value(string query)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Messages/c1/Since" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.Contains("/messages"));
    }

    [Theory]
    [InlineData("/Messages")]
    [InlineData("/Messages/c1")]
    [InlineData("/Messages/c1/Since?after=2026-10-10T09:00:00Z")]
    public async Task Admin_cannot_reach_messages_and_anonymous_is_sent_to_login(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var admin = await host.LoginAsAsync("Admin");

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
        var anonymous = await host.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/chat"));
    }

    [Fact]
    public async Task Admin_gets_403_on_message_posts_too()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var admin = await host.LoginAsAsync("Admin");

        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(admin, "/Messages/c1/Send", new Dictionary<string, string> { ["Form.Text"] = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(admin, "/Messages/WithDoctor/d1")).StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/chat"));
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("Doctor")]
    public async Task Menu_and_side_menu_show_messages_with_the_unread_total_and_no_coming_soon_badge(string role)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var unread = host.Gateway.MapLogged("GET", "/gateway/chat/unread", 200, new { total = 4 });
        host.Gateway.MapJson("GET", "/gateway/patients/me/favorites", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await host.LoginAsAsync(role);

        var html = await (await client.GetAsync(role == "Patient" ? "/Patient" : "/Doctor")).Content.ReadHtmlAsync();

        Assert.Matches(new Regex("href=\"/Messages\"[^>]*>.*?msg-count\"[^>]*>4<", RegexOptions.Singleline), html);
        Assert.True(Regex.Matches(html, "msg-count").Count >= 2, "hem açılır menüde hem kenar menüde rozet olmalı");
        Assert.Equal(1, unread.Count); // iki yerde çizilse de sayı istek başına bir kez sorulur
        Assert.DoesNotContain("yakında", html);
    }

    [Fact]
    public async Task Message_badge_is_hidden_when_the_chat_service_is_down_and_never_asked_for_admin()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/chat/unread", 503);
        var patient = await host.LoginAsAsync("Patient");

        var response = await patient.GetAsync("/Home/About");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("msg-count", await response.Content.ReadHtmlAsync());

        var admin = await host.LoginAsAsync("Admin");
        var before = host.Gateway.Calls("GET", "/gateway/chat/unread").Count();
        await admin.GetAsync("/Home/About");
        Assert.Equal(before, host.Gateway.Calls("GET", "/gateway/chat/unread").Count());
    }

    [Fact]
    public async Task Appointment_detail_offers_send_message_to_the_doctor()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, Appointment(Appt1));
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        host.Gateway.MapJson("GET", "/gateway/payments", 200, EmptyPayments());
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync($"/Patient/Appointments/Details/{Appt1}")).Content.ReadHtmlAsync();

        Assert.Contains("action=\"/Messages/WithDoctor/d1\"", html);
        Assert.Contains("Mesaj gönder", html);
    }

    [Fact]
    public async Task Doctor_profile_page_offers_send_message_only_to_patients()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        host.Gateway.MapJson("GET", "/gateway/doctors/d1/public", 200, Doctor());
        var patient = await host.LoginAsAsync("Patient");
        var doctor = await host.LoginAsAsync("Doctor");

        var forPatient = await (await patient.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();
        var forDoctor = await (await doctor.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();
        var forAnonymous = await (await host.CreateClient().GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.Contains("action=\"/Messages/WithDoctor/d1\"", forPatient);
        Assert.DoesNotContain("/Messages/WithDoctor", forDoctor);
        Assert.DoesNotContain("/Messages/WithDoctor", forAnonymous);
    }
}

// ---------- Menü, avatar, yetki kapsamı ----------
public class SocialShellTests
{
    [Fact]
    public async Task Patient_menu_links_favorites_messages_and_profile_without_coming_soon_badges()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Home/About")).Content.ReadHtmlAsync();

        Assert.Contains("href=\"/Patient/Favorites\"", html);
        Assert.Contains("href=\"/Messages\"", html);
        Assert.Contains("href=\"/Patient/Profile\"", html);
        Assert.Contains("</i>Favoriler</a>", html);
        Assert.DoesNotContain("yakında", html);
        Assert.DoesNotContain("dropdown-item disabled", html);
    }

    [Fact]
    public async Task Navbar_avatar_loads_the_media_photo_and_falls_back_to_initials_on_error()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Home/About")).Content.ReadHtmlAsync();

        Assert.Contains("src=\"/media/users/user-1/photo\"", html);
        Assert.Matches(new Regex("onerror=\"this\\.style\\.display='none'"), html);
        Assert.Contains("AY", html); // baş harf avatarı (ad: Ayşe Yılmaz) yedek olarak sayfada
    }

    [Fact]
    public async Task Social_actions_all_declare_authorization_explicitly()
    {
        await using var host = await WebUiTestHost.StartAsync(withProbe: false);

        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(host.Factory.Services);

        Assert.Empty(missing);
    }
}
