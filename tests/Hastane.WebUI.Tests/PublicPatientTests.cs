using System.Net;
using System.Web;
using static Hastane.WebUI.Tests.PublicPatientTestHelpers;

namespace Hastane.WebUI.Tests;

public class PublicBranchesTests
{
    [Fact]
    public async Task Branches_lists_cards_linking_to_doctors_with_branchId_and_sends_no_token_for_anonymous()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji", "Dahiliye");

        var response = await host.CreateClient().GetAsync("/Branches");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Kardiyoloji", html);
        Assert.Contains("Dahiliye", html);
        Assert.Contains("href=\"/Doctors?branchId=b0\"", html);
        Assert.Equal("", host.Gateway.Requests.Last(r => r.Path == "/gateway/branches").Authorization);
    }

    [Fact]
    public async Task Branches_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches();

        var html = await (await host.CreateClient().GetAsync("/Branches")).Content.ReadHtmlAsync();

        Assert.Contains("Henüz tanımlı branş yok", html);
    }

    [Fact]
    public async Task Branches_service_down_shows_503_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/branches", 503);

        var response = await host.CreateClient().GetAsync("/Branches");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}

public class PublicDoctorSearchTests
{
    [Fact]
    public async Task Search_forwards_every_filter_to_gateway_with_api_parameter_names()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        var search = host.Gateway.MapLogged("GET", "/gateway/doctors/search", 200, Paged([], 0));

        var response = await host.CreateClient().GetAsync(
            "/Doctors?q=kalp%20uzman&branchId=b0&gender=Female&city=Ankara&minFee=100&maxFee=900.5&date=2026-12-14&sort=feeAsc&page=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = HttpUtility.ParseQueryString(search.LastQuery);
        Assert.Equal("kalp uzman", query["text"]);
        Assert.Equal("b0", query["branchIds"]);
        Assert.Equal("Female", query["gender"]);
        Assert.Equal("Ankara", query["city"]);
        Assert.Equal("100", query["minFee"]);
        Assert.Equal("900.5", query["maxFee"]);
        Assert.Equal("2026-12-14", query["date"]);
        Assert.Equal("feeAsc", query["sort"]);
        Assert.Equal("2", query["page"]);
        Assert.Equal("12", query["pageSize"]);
    }

    [Fact]
    public async Task Search_without_filters_sends_only_paging_and_works_anonymously()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        var search = host.Gateway.MapLogged("GET", "/gateway/doctors/search", 200, Paged([], 0));

        await host.CreateClient().GetAsync("/Doctors");

        Assert.Equal("?page=1&pageSize=12", search.LastQuery);
        Assert.Equal("", host.Gateway.Requests.Last(r => r.Path == "/gateway/doctors/search").Authorization);
    }

    [Fact]
    public async Task Search_renders_cards_with_ratings_from_a_single_batch_call()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200,
            Paged([SearchItem("d1", "Ayşe", "Kaya"), SearchItem("d2", "Mehmet", "Demir")], 2));
        var summaries = host.Gateway.MapLogged("GET", "/gateway/reviews/summaries", 200, new object[]
        {
            new { doctorId = "d1", reviewCount = 12, averageRating = 4.5, recommendationPercent = 90 },
        });

        var html = await (await host.CreateClient().GetAsync("/Doctors")).Content.ReadHtmlAsync();

        Assert.Contains("Dr. Ayşe Kaya", html);
        Assert.Contains("Dr. Mehmet Demir", html);
        Assert.Contains("4.5", html);
        Assert.Contains("12 yorum", html);
        Assert.Contains("Henüz yorum yok", html); // d2 özetsiz
        Assert.Contains("href=\"/Doctors/d1\"", html);
        Assert.Equal(1, summaries.Count); // N+1 yok: iki doktor, tek özet çağrısı
        Assert.Equal("d1,d2", HttpUtility.ParseQueryString(summaries.LastQuery)["doctorIds"]);
    }

    [Fact]
    public async Task Search_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, Paged([], 0));

        var html = await (await host.CreateClient().GetAsync("/Doctors?q=yok")).Content.ReadHtmlAsync();

        Assert.Contains("Aramanıza uygun doktor bulunamadı", html);
        Assert.Contains("value=\"yok\"", html); // arama kutusu korunur
    }

    [Fact]
    public async Task Search_still_lists_doctors_when_review_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, Paged([SearchItem("d1", "Ayşe", "Kaya")], 1));
        host.Gateway.MapStatus("GET", "/gateway/reviews/summaries", 503);

        var response = await host.CreateClient().GetAsync("/Doctors");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Dr. Ayşe Kaya", html);
        Assert.Contains("Henüz yorum yok", html);
    }

    [Fact]
    public async Task Search_api_400_is_shown_in_the_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 400,
            Problem(400, "Geçersiz", new { date = new[] { "date yyyy-MM-dd biçiminde geçerli bir tarih olmalı." } }));

        var response = await host.CreateClient().GetAsync("/Doctors?date=bozuk");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("date yyyy-MM-dd biçiminde geçerli bir tarih olmalı.", html);
    }

    [Fact]
    public async Task Search_with_invalid_local_filter_does_not_call_the_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        var search = host.Gateway.MapLogged("GET", "/gateway/doctors/search", 200, Paged([], 0));

        var response = await host.CreateClient().GetAsync("/Doctors?minFee=-5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, search.Count);
        Assert.Contains("Ücret 0 ile 1.000.000 arasında olmalı.", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Search_shows_pager_links_that_keep_the_filters()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, Paged([SearchItem("d1", "Ayşe", "Kaya")], 30, page: 2));

        var html = await (await host.CreateClient().GetAsync("/Doctors?q=kalp&page=2")).Content.ReadHtmlAsync();

        Assert.Contains("2 / 3", html);
        Assert.Contains("page=3", html);
        Assert.Contains("q=kalp", html);
    }
}

public class PublicDoctorProfileTests
{
    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync(string role = "Patient")
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        host.Gateway.MapJson("GET", "/gateway/reviews/doctors/d1/summary", 200,
            new { doctorId = "d1", reviewCount = 2, averageRating = 4.5, recommendationPercent = 100 });
        host.Gateway.MapJson("GET", "/gateway/reviews", 200, new[]
        {
            Review("r1", "Ayşe Yılmaz", 5, "Harika"),
            Review("r2", "Mehmet Kaya", 4, "<script>alert(1)</script> iyi"),
        });
        return (host, await host.LoginAsAsync(role));
    }

    [Fact]
    public async Task Anonymous_is_sent_to_login_without_calling_the_detail_endpoint()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync("/Doctors/d1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login?returnUrl=%2FDoctors%2Fd1", Location(response));
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/doctors/d1"));
    }

    [Fact]
    public async Task Patient_sees_profile_sections_reviews_summary_and_booking_button()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await client.GetAsync("/Doctors/d1");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("Kardiyoloji", html); // branş adı
        Assert.Contains("Ankara Üniversitesi", html); // eğitim
        Assert.Contains("Şehir Hastanesi", html); // deneyim
        Assert.Contains("Yılın Doktoru", html); // ödül
        Assert.Contains("Merkez Klinik", html); // klinik
        Assert.Contains("Pazartesi: 09:00 - 12:00", html); // program
        Assert.Contains("ali@hastane.test", html);
        Assert.Contains("4.5", html);
        Assert.Contains("%100 tavsiye", html);
        Assert.Contains("Harika", html);
        Assert.Contains("Yorum yaz", html);
        Assert.Contains("/Patient/Appointments/Create?doctorId=d1", html);
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/doctors/d1").Last().Authorization);
    }

    [Fact]
    public async Task Review_text_is_html_encoded()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var raw = await (await client.GetAsync("/Doctors/d1")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)</script>", raw);
        Assert.Contains("&lt;script&gt;", raw);
    }

    [Fact]
    public async Task Doctor_role_sees_profile_but_no_review_form_or_booking()
    {
        var (host, client) = await StartAsync("Doctor");
        await using var _h = host;

        var html = await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("Yorumu gönder", html);
        Assert.DoesNotContain("/Patient/Appointments/Create", html);
        Assert.Contains("Randevu almak için hasta hesabıyla giriş yapın", html);
    }

    [Fact]
    public async Task Edit_and_delete_controls_appear_only_on_own_review()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var html = await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.Contains("/Doctors/d1/Reviews/r1/Update", html);
        Assert.Contains("/Doctors/d1/Reviews/r1/Delete", html);
        Assert.DoesNotContain("/Doctors/d1/Reviews/r2/", html);
    }

    [Fact]
    public async Task Review_service_down_still_opens_profile_with_warning()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("GET", "/gateway/reviews", 503);

        var response = await client.GetAsync("/Doctors/d1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Yorumlar şu anda yüklenemiyor", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Unknown_doctor_shows_404_page()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("GET", "/gateway/doctors/yok", 404);

        var response = await client.GetAsync("/Doctors/yok");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Sayfa bulunamadı", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Create_review_posts_only_doctor_rating_and_comment_then_redirects()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("POST", "/gateway/reviews", 201, Review("r9", "Ayşe Yılmaz"));

        var response = await host.PostAsync(client, "/Doctors/d1/Reviews", new()
        {
            ["ReviewForm.Rating"] = "4",
            ["ReviewForm.Comment"] = "Çok iyi",
            ["ReviewerName"] = "Sahte Ad", // yok sayılmalı
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Doctors/d1", Location(response));
        var sent = host.Gateway.Calls("POST", "/gateway/reviews").Single();
        var json = Json(sent.Body);
        Assert.Equal("d1", json.GetProperty("doctorId").GetString());
        Assert.Equal(4, json.GetProperty("rating").GetInt32());
        Assert.Equal("Çok iyi", json.GetProperty("comment").GetString());
        Assert.DoesNotContain("reviewer", sent.Body, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Bearer ", sent.Authorization);

        var next = await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();
        Assert.Contains("Yorumunuz yayımlandı.", next);
    }

    [Fact]
    public async Task Create_review_without_antiforgery_token_is_rejected()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await client.PostAsync("/Doctors/d1/Reviews",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["ReviewForm.Rating"] = "4" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/reviews"));
    }

    [Fact]
    public async Task Create_review_with_out_of_range_rating_shows_error_and_does_not_call_the_api()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await host.PostAsync(client, "/Doctors/d1/Reviews", new() { ["ReviewForm.Rating"] = "9", ["ReviewForm.Comment"] = "x" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Puan 1 ile 5 arasında olmalı.", await response.Content.ReadHtmlAsync());
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/reviews"));
    }

    [Fact]
    public async Task Create_review_api_400_is_shown_on_the_profile_page()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("POST", "/gateway/reviews", 400, Problem(400, "Geçersiz", new { Comment = new[] { "Yorum çok uzun." } }));

        var response = await host.PostAsync(client, "/Doctors/d1/Reviews", new() { ["ReviewForm.Rating"] = "3", ["ReviewForm.Comment"] = "abc" });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Yorum çok uzun.", html);
        Assert.Contains("abc", html); // girilen yorum korunur
    }

    [Fact]
    public async Task Review_actions_are_closed_to_other_roles_and_anonymous()
    {
        var (host, doctorClient) = await StartAsync("Doctor");
        await using var _h = host;

        var forbidden = await host.PostAsync(doctorClient, "/Doctors/d1/Reviews", new() { ["ReviewForm.Rating"] = "4" });
        var anonymous = await host.PostAsync(host.CreateClient(), "/Doctors/d1/Reviews/r1/Delete");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/reviews"));
        Assert.Empty(host.Gateway.Calls("DELETE", "/gateway/reviews/r1"));
    }

    [Fact]
    public async Task Update_review_sends_put_with_id_rating_and_comment()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("PUT", "/gateway/reviews/r1", 204);

        var response = await host.PostAsync(client, "/Doctors/d1/Reviews/r1/Update", new() { ["EditForm.Rating"] = "2", ["EditForm.Comment"] = "Güncel" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var json = Json(host.Gateway.Calls("PUT", "/gateway/reviews/r1").Single().Body);
        Assert.Equal("r1", json.GetProperty("id").GetString());
        Assert.Equal(2, json.GetProperty("rating").GetInt32());
        Assert.Equal("Güncel", json.GetProperty("comment").GetString());
        Assert.Contains("Yorumunuz güncellendi.", await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Update_review_404_from_api_becomes_a_friendly_warning()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("PUT", "/gateway/reviews/r2", 404);

        var response = await host.PostAsync(client, "/Doctors/d1/Reviews/r2/Update", new() { ["EditForm.Rating"] = "2" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Yorum bulunamadı ya da size ait değil.", await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Delete_review_sends_delete_and_confirms()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("DELETE", "/gateway/reviews/r1", 204);

        var response = await host.PostAsync(client, "/Doctors/d1/Reviews/r1/Delete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Single(host.Gateway.Calls("DELETE", "/gateway/reviews/r1"));
        Assert.Contains("Yorumunuz silindi.", await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync());
    }
}

public class PatientAccessTests
{
    public static TheoryData<string> PatientUrls() => new()
    {
        "/Patient",
        "/Patient/Appointments",
        "/Patient/Appointments/Create?doctorId=d1",
        "/Patient/Appointments/Details/11111111-1111-1111-1111-111111111111",
        "/Patient/Prescriptions",
        "/Patient/Prescriptions/Details/33333333-3333-3333-3333-333333333333",
        "/Patient/Payments",
        "/Patient/Payments/Create?appointmentId=11111111-1111-1111-1111-111111111111",
        "/Patient/Payments/Details/44444444-4444-4444-4444-444444444444",
        "/Patient/Invoices",
        "/Patient/Invoices/Details/55555555-5555-5555-5555-555555555555",
    };

    [Theory]
    [MemberData(nameof(PatientUrls))]
    public async Task Anonymous_is_redirected_to_login(string url)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login?returnUrl=", Location(response));
    }

    [Theory]
    [MemberData(nameof(PatientUrls))]
    public async Task Doctor_and_admin_roles_get_403(string url)
    {
        await using var host = await WebUiTestHost.StartAsync();
        foreach (var role in new[] { "Doctor", "Admin" })
        {
            var client = await host.LoginAsAsync(role);

            var response = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Post_actions_reject_wrong_role_and_missing_antiforgery()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var doctor = await host.LoginAsAsync("Doctor");
        var patient = await host.LoginAsAsync("Patient");
        var cancelUrl = $"/Patient/Appointments/Cancel/{Appt1}";
        var payUrl = "/Patient/Payments/Create";

        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(doctor, cancelUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(doctor, payUrl, new() { ["AppointmentId"] = Appt1.ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await patient.PostAsync(cancelUrl, new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await patient.PostAsync(payUrl, new FormUrlEncodedContent([]))).StatusCode);
        Assert.Empty(host.Gateway.Calls("PATCH", $"/gateway/appointments/{Appt1}/status"));
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/payments"));
    }
}

public class PatientHomeTests
{
    [Fact]
    public async Task Home_shows_upcoming_appointments_and_recent_prescriptions_with_links()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var appointments = host.Gateway.MapLogged("GET", "/gateway/appointments", 200, new[]
        {
            Appointment(Appt1, "Confirmed"),
            Appointment(Appt2, "Cancelled"), // iptal olan panelde görünmez
        });
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, new[]
        {
            new { id = Rx1, appointmentId = Appt1, doctorId = DoctorId, issuedAt = DateTime.UtcNow.AddDays(-1), itemCount = 3 },
        });
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Patient");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"/Patient/Appointments/Details/{Appt1}", html);
        Assert.DoesNotContain($"/Patient/Appointments/Details/{Appt2}", html);
        Assert.Contains($"/Patient/Prescriptions/Details/{Rx1}", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("3 ilaç", html);
        Assert.Contains("from=", appointments.LastQuery); // yalnız yaklaşanlar istenir
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/appointments").Last().Authorization);
    }

    [Fact]
    public async Task Home_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Patient")).Content.ReadHtmlAsync();

        Assert.Contains("Yaklaşan randevunuz yok", html);
        Assert.Contains("Henüz reçeteniz yok", html);
    }

    [Fact]
    public async Task Home_survives_one_service_being_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/appointments", 503);
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Patient");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Randevular şu anda yüklenemiyor", html);
        Assert.Contains("Henüz reçeteniz yok", html);
    }
}

public class PatientAppointmentTests
{
    [Fact]
    public async Task List_renders_rows_uses_one_doctor_batch_call_and_never_sends_patientId()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var list = host.Gateway.MapLogged("GET", "/gateway/appointments", 200, new[] { Appointment(Appt1, "Pending"), Appointment(Appt2, "Completed") });
        var batch = host.Gateway.MapLogged("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Patient/Appointments")).Content.ReadHtmlAsync();

        Assert.Contains("Onay bekliyor", html);
        Assert.Contains("Tamamlandı", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Equal("", list.LastQuery);
        Assert.DoesNotContain("patientId", list.LastQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, batch.Count); // iki randevu, aynı doktor: tek toplu çağrı
        Assert.Equal("d1", HttpUtility.ParseQueryString(batch.LastQuery)["ids"]);
    }

    [Fact]
    public async Task List_forwards_a_known_status_filter_and_drops_unknown_ones()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var list = host.Gateway.MapLogged("GET", "/gateway/appointments", 200, Array.Empty<object>());
        var client = await host.LoginAsAsync("Patient");

        await client.GetAsync("/Patient/Appointments?status=Pending");
        Assert.Equal("?status=Pending", list.LastQuery);

        await client.GetAsync("/Patient/Appointments?status=hack");
        Assert.Equal("", list.LastQuery);
    }

    [Fact]
    public async Task List_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        var client = await host.LoginAsAsync("Patient");

        Assert.Contains("Henüz randevunuz yok", await (await client.GetAsync("/Patient/Appointments")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Details_shows_visit_notes_payment_and_cancel_button_for_pending()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, AppointmentWithDetail(Appt1, "Pending"));
        var payments = host.Gateway.MapLogged("GET", "/gateway/payments", 200, new { items = new[] { Payment(Pay1, "Paid") }, totalCount = 1, page = 1, pageSize = 10 });
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync($"/Patient/Appointments/Details/{Appt1}")).Content.ReadHtmlAsync();

        Assert.Contains("Göğüs ağrısı", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("Randevuyu iptal et", html);
        Assert.Contains("Ödendi", html);
        Assert.DoesNotContain(">Ödeme yap<", html); // ödenmiş
        Assert.Contains($"appointmentId={Appt1}", payments.LastQuery);
    }

    [Fact]
    public async Task Details_of_cancelled_appointment_has_no_cancel_or_pay_buttons()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, AppointmentWithDetail(Appt1, "Cancelled"));
        host.Gateway.MapJson("GET", "/gateway/payments", 200, EmptyPayments());
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync($"/Patient/Appointments/Details/{Appt1}")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("Randevuyu iptal et", html);
        Assert.DoesNotContain(">Ödeme yap<", html);
        Assert.Contains("İptal edildi", html);
    }

    [Fact]
    public async Task Details_of_someone_elses_appointment_shows_404_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", $"/gateway/appointments/{Appt1}", 404);
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync($"/Patient/Appointments/Details/{Appt1}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_sends_patch_with_Cancelled_status_and_redirects_back()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("PATCH", $"/gateway/appointments/{Appt1}/status", 200, Appointment(Appt1, "Cancelled"));
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, $"/Patient/Appointments/Cancel/{Appt1}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Appointments/Details/{Appt1}", Location(response));
        var sent = host.Gateway.Calls("PATCH", $"/gateway/appointments/{Appt1}/status").Single();
        Assert.Equal("Cancelled", Json(sent.Body).GetProperty("status").GetString());
        Assert.StartsWith("Bearer ", sent.Authorization);
    }

    [Fact]
    public async Task Cancel_409_from_state_machine_is_shown_as_warning()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("PATCH", $"/gateway/appointments/{Appt1}/status", 409, Problem(409, "Completed durumundan Cancelled durumuna geçilemez."));
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, AppointmentWithDetail(Appt1, "Completed"));
        host.Gateway.MapJson("GET", "/gateway/payments", 200, EmptyPayments());
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        var client = await host.LoginAsAsync("Patient");

        await host.PostAsync(client, $"/Patient/Appointments/Cancel/{Appt1}");
        var html = await (await client.GetAsync($"/Patient/Appointments/Details/{Appt1}")).Content.ReadHtmlAsync();

        Assert.Contains("Completed durumundan Cancelled durumuna geçilemez.", html);
    }

    [Fact]
    public async Task Create_form_shows_doctor_and_schedule()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Patient/Appointments/Create?doctorId=d1")).Content.ReadHtmlAsync();

        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("Pazartesi: 09:00 - 12:00 (30 dk)", html);
        Assert.Contains("datetime-local", html);
        Assert.Contains("name=\"DoctorId\"", html);
    }

    [Fact]
    public async Task Create_without_doctor_redirects_to_doctor_search()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync("/Patient/Appointments/Create");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Doctors", Location(response));
    }

    [Fact]
    public async Task Create_posts_utc_date_with_branch_from_api_and_ignores_patient_and_branch_from_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        host.Gateway.MapJson("POST", "/gateway/appointments", 201, Appointment(Appt1));
        var client = await host.LoginAsAsync("Patient");
        var localText = LocalFuture();
        var expectedUtc = DateTime.SpecifyKind(DateTime.Parse(localText, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Local).ToUniversalTime();

        var response = await host.PostAsync(client, "/Patient/Appointments/Create", new()
        {
            ["DoctorId"] = "d1",
            ["AppointmentLocal"] = localText,
            ["BranchId"] = "kotu-sube",
            ["PatientId"] = "baska-hasta",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Appointments/Details/{Appt1}", Location(response));
        var sent = host.Gateway.Calls("POST", "/gateway/appointments").Single();
        var json = Json(sent.Body);
        Assert.Equal("d1", json.GetProperty("doctorId").GetString());
        Assert.Equal("b0", json.GetProperty("branchId").GetString());
        Assert.False(json.TryGetProperty("patientId", out _));
        var sentDate = json.GetProperty("appointmentDate").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, sentDate.Kind);
        Assert.Equal(expectedUtc, sentDate);
    }

    [Fact]
    public async Task Create_409_slot_taken_is_shown_in_form_and_keeps_the_entered_date()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        host.Gateway.MapJson("POST", "/gateway/appointments", 409, Problem(409, "Bu doktorun bu saatte başka bir randevusu var."));
        var client = await host.LoginAsAsync("Patient");
        var localText = LocalFuture();

        var response = await host.PostAsync(client, "/Patient/Appointments/Create", new() { ["DoctorId"] = "d1", ["AppointmentLocal"] = localText });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bu doktorun bu saatte başka bir randevusu var.", html);
        Assert.Contains(localText, html);
    }

    [Fact]
    public async Task Create_400_validation_errors_are_shown_in_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        host.Gateway.MapJson("POST", "/gateway/appointments", 400, Problem(400, "Geçersiz", new { AppointmentDate = new[] { "Randevu tarihi gelecekte olmalı." } }));
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostAsync(client, "/Patient/Appointments/Create", new() { ["DoctorId"] = "d1", ["AppointmentLocal"] = LocalFuture() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Randevu tarihi gelecekte olmalı.", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Create_with_past_or_missing_date_never_reaches_the_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        var client = await host.LoginAsAsync("Patient");
        var past = DateTime.Now.AddDays(-2).ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var pastHtml = await (await host.PostAsync(client, "/Patient/Appointments/Create", new() { ["DoctorId"] = "d1", ["AppointmentLocal"] = past })).Content.ReadHtmlAsync();
        var missingHtml = await (await host.PostAsync(client, "/Patient/Appointments/Create", new() { ["DoctorId"] = "d1" })).Content.ReadHtmlAsync();

        Assert.Contains("Randevu tarihi gelecekte olmalı.", pastHtml);
        Assert.Contains("Tarih ve saat seçin.", missingHtml);
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/appointments"));
    }
}

public class PatientPrescriptionTests
{
    [Fact]
    public async Task List_and_details_render_items()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, new[]
        {
            new { id = Rx1, appointmentId = Appt1, doctorId = DoctorId, issuedAt = DateTime.UtcNow.AddDays(-1), itemCount = 2 },
        });
        host.Gateway.MapJson("GET", $"/gateway/prescriptions/{Rx1}", 200, new
        {
            id = Rx1,
            appointmentId = Appt1,
            doctorId = DoctorId,
            patientId = "user-1",
            issuedAt = DateTime.UtcNow.AddDays(-1),
            notes = "Bol su için",
            items = new[]
            {
                new { id = Guid.NewGuid(), lineNo = 1, medicineName = "Parasetamol", dosage = "500 mg", frequency = "Günde 3", durationDays = 5, instructions = (string?)"Tok karnına" },
                new { id = Guid.NewGuid(), lineNo = 2, medicineName = "Vitamin C", dosage = "1000 mg", frequency = "Günde 1", durationDays = 10, instructions = (string?)null },
            },
        });
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        var client = await host.LoginAsAsync("Patient");

        var list = await (await client.GetAsync("/Patient/Prescriptions")).Content.ReadHtmlAsync();
        var detail = await (await client.GetAsync($"/Patient/Prescriptions/Details/{Rx1}")).Content.ReadHtmlAsync();

        Assert.Contains("Dr. Ali Veli", list);
        Assert.Contains($"/Patient/Prescriptions/Details/{Rx1}", list);
        Assert.Contains("Parasetamol", detail);
        Assert.Contains("Vitamin C", detail);
        Assert.Contains("Günde 3", detail);
        Assert.Contains("5 gün", detail);
        Assert.Contains("Bol su için", detail);
        Assert.Contains("Tok karnına", detail);
    }

    [Fact]
    public async Task List_empty_state_and_missing_prescription_404()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        host.Gateway.MapStatus("GET", $"/gateway/prescriptions/{Rx1}", 404);
        var client = await host.LoginAsAsync("Patient");

        Assert.Contains("Henüz reçeteniz yok", await (await client.GetAsync("/Patient/Prescriptions")).Content.ReadHtmlAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Patient/Prescriptions/Details/{Rx1}")).StatusCode);
    }
}

public class PatientBillingListTests
{
    [Fact]
    public async Task Payments_list_sends_paging_renders_rows_and_pager()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var payments = host.Gateway.MapLogged("GET", "/gateway/payments", 200,
            new { items = new[] { Payment(Pay1, "Paid") }, totalCount = 25, page = 2, pageSize = 10 });
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Patient/Payments?page=2")).Content.ReadHtmlAsync();

        var query = HttpUtility.ParseQueryString(payments.LastQuery);
        Assert.Equal("2", query["page"]);
        Assert.Equal("10", query["pageSize"]);
        Assert.Null(query["patientId"]);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("500,00 TRY", html);
        Assert.Contains("INV-0001", html);
        Assert.Contains("2 / 3", html);
    }

    [Fact]
    public async Task Payments_empty_state_and_details_never_show_a_full_card_number()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/payments", 200, EmptyPayments());
        host.Gateway.MapJson("GET", $"/gateway/payments/{Pay1}", 200, Payment(Pay1, "Paid"));
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        var client = await host.LoginAsAsync("Patient");

        var empty = await (await client.GetAsync("/Patient/Payments")).Content.ReadHtmlAsync();
        var detail = await (await client.GetAsync($"/Patient/Payments/Details/{Pay1}")).Content.ReadHtmlAsync();

        Assert.Contains("Henüz ödemeniz yok", empty);
        Assert.Contains("Visa **** 1111", detail);
        Assert.Contains("Ödendi", detail);
    }

    [Fact]
    public async Task Invoices_list_and_details_with_lines_and_totals()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var invoices = host.Gateway.MapLogged("GET", "/gateway/invoices", 200, new
        {
            items = new[] { new { id = Inv1, invoiceNumber = "INV-0001", paymentId = Pay1, patientId = "user-1", doctorId = DoctorId, issuedAt = DateTime.UtcNow, currency = "TRY", subtotal = 454.55m, taxPercent = 10m, taxAmount = 45.45m, total = 500m } },
            totalCount = 1,
            page = 1,
            pageSize = 10,
        });
        host.Gateway.MapJson("GET", $"/gateway/invoices/{Inv1}", 200, new
        {
            id = Inv1,
            invoiceNumber = "INV-0001",
            paymentId = Pay1,
            patientId = "user-1",
            doctorId = DoctorId,
            issuedAt = DateTime.UtcNow,
            currency = "TRY",
            subtotal = 454.55m,
            taxPercent = 10m,
            taxAmount = 45.45m,
            total = 500m,
            lines = new[] { new { id = Guid.NewGuid(), description = "Muayene ücreti", quantity = 1, unitPrice = 454.55m, lineTotal = 454.55m } },
        });
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        var client = await host.LoginAsAsync("Patient");

        var list = await (await client.GetAsync("/Patient/Invoices")).Content.ReadHtmlAsync();
        var detail = await (await client.GetAsync($"/Patient/Invoices/Details/{Inv1}")).Content.ReadHtmlAsync();

        Assert.Equal("?page=1&pageSize=10", invoices.LastQuery);
        Assert.Contains("INV-0001", list);
        Assert.Contains("500,00 TRY", list);
        Assert.Contains("Muayene ücreti", detail);
        Assert.Contains("KDV (%10)", detail);
        Assert.Contains("454,55 TRY", detail);
        Assert.Contains("Toplam", detail);
    }

    [Fact]
    public async Task Invoices_empty_state_and_404()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/invoices", 200, EmptyPayments());
        host.Gateway.MapStatus("GET", $"/gateway/invoices/{Inv1}", 404);
        var client = await host.LoginAsAsync("Patient");

        Assert.Contains("Henüz faturanız yok", await (await client.GetAsync("/Patient/Invoices")).Content.ReadHtmlAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Patient/Invoices/Details/{Inv1}")).StatusCode);
    }
}

public class PatientPaymentFormTests
{
    private const string TestCard = "4111 1111 1111 1111";
    private const string TestCardDigits = "4111111111111111";

    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync(string status = "Confirmed", decimal? fee = 500m)
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, AppointmentWithDetail(Appt1, status));
        host.Gateway.MapJson("GET", "/gateway/payments", 200, EmptyPayments()); // ödenmiş yok
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor(minFee: fee));
        return (host, await host.LoginAsAsync("Patient"));
    }

    private static async Task<(string key, Dictionary<string, string> fields)> OpenFormAsync(HttpClient client)
    {
        var html = await (await client.GetAsync($"/Patient/Payments/Create?appointmentId={Appt1}")).Content.ReadAsStringAsync();
        var key = ExtractIdempotencyKey(html);
        return (key, new Dictionary<string, string>
        {
            ["AppointmentId"] = Appt1.ToString(),
            ["IdempotencyKey"] = key,
            ["CardNumber"] = TestCard,
            ["ExpMonth"] = "12",
            ["ExpYear"] = "2030",
            ["Cvv"] = "123",
            ["HolderName"] = "Ayşe Yılmaz",
        });
    }

    [Fact]
    public async Task Form_shows_amount_from_doctor_fee_and_a_fresh_idempotency_key_each_time()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var first = await (await client.GetAsync($"/Patient/Payments/Create?appointmentId={Appt1}")).Content.ReadAsStringAsync();
        var second = await (await client.GetAsync($"/Patient/Payments/Create?appointmentId={Appt1}")).Content.ReadAsStringAsync();

        Assert.Contains("500,00 TRY", WebUtility.HtmlDecode(first));
        Assert.Contains("Dr. Ali Veli", WebUtility.HtmlDecode(first));
        Assert.Matches("^[A-Za-z0-9_-]{16,100}$", ExtractIdempotencyKey(first));
        Assert.NotEqual(ExtractIdempotencyKey(first), ExtractIdempotencyKey(second));
    }

    [Fact]
    public async Task Form_response_is_not_cacheable()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await client.GetAsync($"/Patient/Payments/Create?appointmentId={Appt1}");

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Form_for_cancelled_appointment_redirects_with_warning()
    {
        var (host, client) = await StartAsync("Cancelled");
        await using var _h = host;

        var response = await client.GetAsync($"/Patient/Payments/Create?appointmentId={Appt1}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Appointments/Details/{Appt1}", Location(response));
    }

    [Fact]
    public async Task Form_for_already_paid_appointment_redirects_to_that_payment()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("GET", "/gateway/payments", 200, new { items = new[] { Payment(Pay1, "Paid") }, totalCount = 1, page = 1, pageSize = 1 });

        var response = await client.GetAsync($"/Patient/Payments/Create?appointmentId={Appt1}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Payments/Details/{Pay1}", Location(response));
    }

    [Fact]
    public async Task Form_for_doctor_without_fee_redirects_back()
    {
        var (host, client) = await StartAsync(fee: null);
        await using var _h = host;

        var response = await client.GetAsync($"/Patient/Payments/Create?appointmentId={Appt1}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Appointments/Details/{Appt1}", Location(response));
    }

    [Fact]
    public async Task Post_sends_header_key_server_computed_amount_and_clean_card_then_redirects_to_payment()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var created = host.Gateway.MapLogged("POST", "/gateway/payments", 201, Payment(Pay1, "Paid"));
        var (key, fields) = await OpenFormAsync(client);
        fields["Amount"] = "1"; // formu kurcalama: yok sayılmalı
        fields["PatientId"] = "baska-hasta";

        var response = await host.PostAsync(client, "/Patient/Payments/Create", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Payments/Details/{Pay1}", Location(response));
        Assert.Equal(key, created.Keys.Single());
        var sent = host.Gateway.Calls("POST", "/gateway/payments").Single();
        Assert.StartsWith("Bearer ", sent.Authorization); // başlıklı istek de oturum belirtecini taşır
        var json = Json(sent.Body);
        Assert.Equal(Appt1.ToString(), json.GetProperty("appointmentId").GetString());
        Assert.Equal("d1", json.GetProperty("doctorId").GetString());
        Assert.Equal(500m, json.GetProperty("amount").GetDecimal());
        Assert.Equal("TRY", json.GetProperty("currency").GetString());
        Assert.False(json.TryGetProperty("patientId", out _));
        var card = json.GetProperty("card");
        Assert.Equal(TestCardDigits, card.GetProperty("number").GetString());
        Assert.Equal(12, card.GetProperty("expMonth").GetInt32());
        Assert.Equal(2030, card.GetProperty("expYear").GetInt32());
        Assert.Equal("123", card.GetProperty("cvv").GetString());
        Assert.Equal("Ayşe Yılmaz", card.GetProperty("holderName").GetString());
    }

    [Fact]
    public async Task Double_submit_sends_the_same_idempotency_key_both_times()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var created = host.Gateway.MapLogged("POST", "/gateway/payments", 201, Payment(Pay1, "Paid"));
        var (key, fields) = await OpenFormAsync(client);

        var first = await host.PostAsync(client, "/Patient/Payments/Create", new Dictionary<string, string>(fields));
        var second = await host.PostAsync(client, "/Patient/Payments/Create", new Dictionary<string, string>(fields));

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal(2, created.Count);
        Assert.All(created.Keys, k => Assert.Equal(key, k));
    }

    [Fact]
    public async Task Missing_or_malformed_key_is_replaced_with_a_valid_one()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var created = host.Gateway.MapLogged("POST", "/gateway/payments", 201, Payment(Pay1, "Paid"));
        var (_, fields) = await OpenFormAsync(client);
        fields["IdempotencyKey"] = "1";

        await host.PostAsync(client, "/Patient/Payments/Create", fields);

        Assert.Matches("^[A-Za-z0-9_-]{16,100}$", created.Keys.Single());
    }

    [Fact]
    public async Task Declined_card_402_shows_message_rotates_the_key_and_never_echoes_the_card()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("POST", "/gateway/payments", 402, Payment(Pay1, "Failed"));
        var (oldKey, fields) = await OpenFormAsync(client);

        var response = await host.PostAsync(client, "/Patient/Payments/Create", fields);
        var raw = await response.Content.ReadAsStringAsync();
        var html = WebUtility.HtmlDecode(raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("reddedildi", html);
        Assert.NotEqual(oldKey, ExtractIdempotencyKey(raw)); // reddedilen niyet kapandı: yeni deneme yeni anahtar
        AssertNoCardEcho(raw);
    }

    [Fact]
    public async Task Api_400_is_shown_keeps_the_key_and_never_echoes_the_card()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("POST", "/gateway/payments", 400, Problem(400, "Geçersiz kart numarası."));
        var (key, fields) = await OpenFormAsync(client);

        var response = await host.PostAsync(client, "/Patient/Payments/Create", fields);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Geçersiz kart numarası.", WebUtility.HtmlDecode(raw));
        Assert.Equal(key, ExtractIdempotencyKey(raw));
        AssertNoCardEcho(raw);
    }

    [Fact]
    public async Task Api_409_already_paid_is_shown_and_keeps_the_key()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("POST", "/gateway/payments", 409, Problem(409, "Bu randevu için ödeme zaten yapılmış."));
        var (key, fields) = await OpenFormAsync(client);

        var raw = await (await host.PostAsync(client, "/Patient/Payments/Create", fields)).Content.ReadAsStringAsync();

        Assert.Contains("Bu randevu için ödeme zaten yapılmış.", WebUtility.HtmlDecode(raw));
        Assert.Equal(key, ExtractIdempotencyKey(raw));
        AssertNoCardEcho(raw);
    }

    [Fact]
    public async Task Provider_unreachable_502_shows_retry_hint_on_the_form_with_the_same_key()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("POST", "/gateway/payments", 502, Problem(502, "Ödeme işlenemedi"));
        var (key, fields) = await OpenFormAsync(client);

        var response = await host.PostAsync(client, "/Patient/Payments/Create", fields);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("iki kez tahsil edilmez", WebUtility.HtmlDecode(raw));
        Assert.Equal(key, ExtractIdempotencyKey(raw));
        AssertNoCardEcho(raw);
    }

    [Fact]
    public async Task Local_validation_blocks_bad_card_data_before_calling_the_api_and_does_not_echo_it()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var created = host.Gateway.MapLogged("POST", "/gateway/payments", 201, Payment(Pay1, "Paid"));
        var (_, fields) = await OpenFormAsync(client);
        fields["CardNumber"] = "4111-abcd";
        fields["Cvv"] = "12";

        var response = await host.PostAsync(client, "/Patient/Payments/Create", fields);
        var raw = await response.Content.ReadAsStringAsync();
        var html = WebUtility.HtmlDecode(raw);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Kart numarası 12-19 haneli rakam olmalı.", html);
        Assert.Contains("CVV 3 veya 4 haneli olmalı.", html);
        Assert.Equal(0, created.Count);
        Assert.DoesNotContain("4111", raw);
    }

    [Fact]
    public async Task Missing_card_fields_show_required_messages()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var (_, fields) = await OpenFormAsync(client);
        fields.Remove("CardNumber");
        fields.Remove("ExpMonth");
        fields.Remove("HolderName");

        var html = await (await host.PostAsync(client, "/Patient/Payments/Create", fields)).Content.ReadHtmlAsync();

        Assert.Contains("Kart numarası gerekli.", html);
        Assert.Contains("Ay gerekli.", html);
        Assert.Contains("Kart üzerindeki ad gerekli.", html);
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/payments"));
    }

    private static void AssertNoCardEcho(string rawHtml)
    {
        Assert.DoesNotContain("4111", rawHtml);
        Assert.DoesNotContain("1111 1111", rawHtml);
        Assert.DoesNotContain("value=\"123\"", rawHtml); // CVV geri basılmaz
        Assert.DoesNotContain("name=\"CardNumber\" value=", rawHtml);
    }
}
