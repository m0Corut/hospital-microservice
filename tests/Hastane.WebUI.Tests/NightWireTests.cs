using System.Net;
using Hastane.TestSupport;
using static Hastane.WebUI.Tests.PublicPatientTestHelpers;
using static Hastane.WebUI.Tests.SlotTestHelpers;
using AdminH = Hastane.WebUI.Tests.AdminPanelTestHelpers;

namespace Hastane.WebUI.Tests;

// Gece N4: saat seçici (durumlar), herkese açık doktor profili, yorum sahipliği (isMine), randevu notu, hesap ayarları, admin kullanıcı listesi.

public class SlotPickerStateTests
{
    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync()
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        return (host, await host.LoginAsAsync("Patient"));
    }

    [Fact]
    public async Task Without_date_the_first_day_is_requested()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var availability = host.Gateway.MapAvailability(SlotUtc(0, 23, 30));

        await client.GetAsync("/Patient/Appointments/Create?doctorId=d1");

        Assert.Equal($"?doctorId=d1&date={DayText(0)}", availability.LastQuery);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2020-01-01")] // geçmiş gün
    [InlineData("2099-01-01")] // 14 günün dışı
    public async Task Invalid_or_out_of_range_date_falls_back_to_today(string date)
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var availability = host.Gateway.MapAvailability();

        var response = await client.GetAsync($"/Patient/Appointments/Create?doctorId=d1&date={date}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"?doctorId=d1&date={DayText(0)}", availability.LastQuery);
    }

    [Fact]
    public async Task Last_day_in_range_is_accepted()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var availability = host.Gateway.MapAvailability();

        await client.GetAsync($"/Patient/Appointments/Create?doctorId=d1&date={DayText(13)}");

        Assert.Equal($"?doctorId=d1&date={DayText(13)}", availability.LastQuery);
    }

    [Fact]
    public async Task Empty_day_says_no_free_hours_and_offers_the_next_day()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapAvailability();

        var html = await (await client.GetAsync($"/Patient/Appointments/Create?doctorId=d1&date={DayText(2)}")).Content.ReadHtmlAsync();

        Assert.Contains("Bu gün için boş saat yok", html);
        Assert.Contains($"Sonraki gün", html);
        Assert.Contains($"date={DayText(3)}", html);
        Assert.DoesNotContain("name=\"SlotStart\"", html);
    }

    [Fact]
    public async Task Last_day_empty_has_no_next_day_link_but_day_buttons_remain()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapAvailability();

        var html = await (await client.GetAsync($"/Patient/Appointments/Create?doctorId=d1&date={DayText(13)}")).Content.ReadHtmlAsync();

        Assert.Contains("Bu gün için boş saat yok", html);
        Assert.DoesNotContain("Sonraki gün", html);
        Assert.Contains($"date={DayText(0)}", html);
    }

    [Fact]
    public async Task Availability_503_shows_a_warning_and_the_page_still_opens()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("GET", "/gateway/appointments/availability", 503);

        var response = await client.GetAsync($"/Patient/Appointments/Create?doctorId=d1&date={DayText(2)}");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Doktorun boş saatleri şu an alınamıyor", html);
        Assert.DoesNotContain("name=\"SlotStart\"", html);
    }

    [Fact]
    public async Task Unknown_doctor_in_availability_shows_404_page()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("GET", "/gateway/appointments/availability", 404);

        var response = await client.GetAsync($"/Patient/Appointments/Create?doctorId=d1&date={DayText(2)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Slot_times_use_istanbul_time_regardless_of_server_time_zone()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        // 21:30 UTC = ertesi gün 00:30 İstanbul; 20:59 UTC = 23:59 İstanbul
        host.Gateway.MapAvailability(SlotUtc(2, 0, 30), SlotUtc(2, 23, 59));

        var html = await (await client.GetAsync($"/Patient/Appointments/Create?doctorId=d1&date={DayText(2)}")).Content.ReadHtmlAsync();

        Assert.Contains(">00:30</label>", html);
        Assert.Contains(">23:59</label>", html);
        Assert.Contains($"value=\"{SlotValue(SlotUtc(2, 0, 30))}\"", html); // POST'a gidecek değer UTC kalır
    }

    [Fact]
    public async Task Slot_picker_requires_patient_role_on_create()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var doctor = await host.LoginAsAsync("Doctor");

        var response = await doctor.GetAsync($"/Patient/Appointments/Create?doctorId=d1&date={DayText(2)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/appointments/availability"));
    }
}

public class AppointmentNoteDisplayTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string NoteText = "<b>Gece</b> nöbet sonrası ağrı";

    private static object WithNote(string patientId = "user-1", string doctorId = "doc-1", string? note = NoteText) => new
    {
        id = Id,
        doctorId,
        patientId,
        branchId = "b0",
        appointmentDate = DateTime.UtcNow.AddDays(3),
        status = "Pending",
        createdAt = DateTime.UtcNow.AddDays(-1),
        note,
    };

    [Fact]
    public async Task Patient_details_show_the_note_encoded()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Id}", 200, WithNote(doctorId: "d1"));
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        host.Gateway.MapJson("GET", "/gateway/payments", 200, EmptyPayments());
        var client = await host.LoginAsAsync("Patient");

        var response = await client.GetAsync($"/Patient/Appointments/Details/{Id}");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Şikâyet / not", WebUtility.HtmlDecode(raw));
        Assert.Contains("ağrı", WebUtility.HtmlDecode(raw));
        Assert.DoesNotContain("<b>Gece</b>", raw);
        Assert.Contains("&lt;b&gt;Gece&lt;/b&gt;", raw);
    }

    [Fact]
    public async Task Patient_details_without_note_hide_the_row()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Id}", 200, WithNote(doctorId: "d1", note: null));
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        host.Gateway.MapJson("GET", "/gateway/payments", 200, EmptyPayments());
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync($"/Patient/Appointments/Details/{Id}")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("Şikâyet / not", html);
    }

    [Fact]
    public async Task Doctor_details_show_the_patient_note()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/users", 200, new[] { new { id = "user-1", firstName = "Zeynep", lastName = "Kaya" } });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { new { id = "b0", name = "Kardiyoloji" } });
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Id}", 200, WithNote());
        var client = host.CreateClient();
        await host.LoginAsync(client, TestJwt.Create("user-1", ["Doctor"], doctorId: "doc-1"), roles: ["Doctor"]);

        var raw = await (await client.GetAsync($"/Doctor/Appointments/Details/{Id}")).Content.ReadAsStringAsync();

        Assert.Contains("Hasta notu", raw);
        Assert.Contains("&lt;b&gt;Gece&lt;/b&gt;", raw);
        Assert.DoesNotContain("<b>Gece</b>", raw);
    }

    [Fact]
    public async Task Admin_details_show_the_patient_note()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { AdminH.DoctorListItem() });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { AdminH.Branch() });
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Id}", 200, WithNote());
        var client = await AdminH.AdminClientAsync(host);

        var raw = await (await client.GetAsync($"/Admin/Appointments/Details/{Id}")).Content.ReadAsStringAsync();

        Assert.Contains("Hasta notu", raw);
        Assert.Contains("&lt;b&gt;Gece&lt;/b&gt;", raw);
        Assert.DoesNotContain("<b>Gece</b>", raw);
    }
}

// Tüm ekran saatleri Europe/Istanbul ile gösterilir: sunucu saat diliminden bağımsız (UTC 06:00 -> 09:00).
public class ClinicTimeDisplayTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime At = new(2026, 10, 20, 6, 0, 0, DateTimeKind.Utc);

    private static object Appt(string? note = null) => new
    {
        id = Id,
        doctorId = "doc-1",
        patientId = "user-1",
        branchId = "b0",
        appointmentDate = At,
        status = "Pending",
        createdAt = At.AddDays(-1),
        note,
    };

    [Fact]
    public async Task Patient_details_and_confirmation_show_istanbul_time_and_confirmation_shows_the_note()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Id}", 200, Appt("Kontrol muayenesi"));
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        host.Gateway.MapJson("GET", "/gateway/payments", 200, EmptyPayments());
        var client = await host.LoginAsAsync("Patient");

        var details = await (await client.GetAsync($"/Patient/Appointments/Details/{Id}")).Content.ReadHtmlAsync();
        var confirmation = await (await client.GetAsync($"/Patient/Appointments/Confirmation/{Id}")).Content.ReadHtmlAsync();

        Assert.Contains("20.10.2026 09:00", details);
        Assert.Contains("20.10.2026 09:00", confirmation);
        Assert.Contains("Şikâyet / not", confirmation);
        Assert.Contains("Kontrol muayenesi", confirmation);
    }

    [Fact]
    public async Task Admin_and_doctor_details_show_istanbul_time()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { AdminH.DoctorListItem() });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { AdminH.Branch() });
        host.Gateway.MapJson("GET", "/gateway/users", 200, new[] { new { id = "user-1", firstName = "Zeynep", lastName = "Kaya" } });
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Id}", 200, Appt());
        var admin = await AdminH.AdminClientAsync(host);
        var doctor = host.CreateClient();
        await host.LoginAsync(doctor, TestJwt.Create("user-1", ["Doctor"], doctorId: "doc-1"), roles: ["Doctor"]);

        Assert.Contains("20.10.2026 09:00", await (await admin.GetAsync($"/Admin/Appointments/Details/{Id}")).Content.ReadHtmlAsync());
        Assert.Contains("20.10.2026 09:00", await (await doctor.GetAsync($"/Doctor/Appointments/Details/{Id}")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Calendar_uses_istanbul_days_for_the_week_range_and_the_column_of_late_evening_utc_appointments()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/users", 200, new[] { new { id = "user-1", firstName = "Zeynep", lastName = "Kaya" } });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { AdminH.Branch("b0") });
        // 13 Ekim 21:30 UTC = 14 Ekim 00:30 İstanbul
        var late = new DateTime(2026, 10, 13, 21, 30, 0, DateTimeKind.Utc);
        var query = host.Gateway.MapLogged("GET", "/gateway/appointments", 200, new[]
        {
            new { id = Id, doctorId = "doc-1", patientId = "user-1", branchId = "b0", appointmentDate = late, status = "Pending", createdAt = late.AddDays(-1) },
        });
        var client = host.CreateClient();
        await host.LoginAsync(client, TestJwt.Create("user-1", ["Doctor"], doctorId: "doc-1"), roles: ["Doctor"]);

        var html = await (await client.GetAsync("/Doctor/Calendar?start=2026-10-14")).Content.ReadHtmlAsync();

        Assert.Contains("00:30", html);
        var q = System.Web.HttpUtility.ParseQueryString(query.LastQuery);
        var from = DateTime.Parse(q["from"]!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
        Assert.Equal(new DateTime(2026, 10, 11, 21, 0, 0, DateTimeKind.Utc), from); // Pzt 12 Ekim 00:00 İstanbul
    }
}

public class AnonymousDoctorProfileTests
{
    private static async Task<WebUiTestHost> StartAsync()
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/d1/public", 200, PublicDoctor());
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        host.Gateway.MapJson("GET", "/gateway/reviews/doctors/d1/summary", 200, new { doctorId = "d1", reviewCount = 1, averageRating = 5.0, recommendationPercent = 100 });
        host.Gateway.MapJson("GET", "/gateway/reviews", 200, new[] { Review("r1", "Ayşe Yılmaz", 5, "Harika", isMine: false) });
        return host;
    }

    [Fact]
    public async Task Anonymous_gets_200_calls_public_endpoint_and_shows_no_phone_or_email()
    {
        await using var host = await StartAsync();

        var response = await host.CreateClient().GetAsync("/Doctors/d1");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("Merkez Klinik", html);
        Assert.DoesNotContain("0555 000 00 00", html);
        Assert.DoesNotContain("ali@hastane.test", html);
        Assert.Contains("Telefon ve e-posta bilgisi için giriş yapın", html);
        Assert.Single(host.Gateway.Calls("GET", "/gateway/doctors/d1/public"));
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/doctors/d1"));
    }

    [Fact]
    public async Task Anonymous_book_button_goes_to_login_with_return_to_the_booking_form()
    {
        await using var host = await StartAsync();

        var html = await (await host.CreateClient().GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.Contains("href=\"/Account/Login?returnUrl=%2FPatient%2FAppointments%2FCreate%3FdoctorId%3Dd1\"", html);
        Assert.DoesNotContain("href=\"/Patient/Appointments/Create", html); // girişsiz doğrudan form bağlantısı yok
        Assert.DoesNotContain("Yorumu gönder", html);
    }

    [Fact]
    public async Task Anonymous_sees_reviews_without_edit_or_delete_controls()
    {
        await using var host = await StartAsync();

        var html = await (await host.CreateClient().GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.Contains("Harika", html);
        Assert.DoesNotContain("/Reviews/r1/Update", html);
        Assert.DoesNotContain("/Reviews/r1/Delete", html);
    }

    [Fact]
    public async Task Anonymous_unknown_doctor_shows_404_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/doctors/yok/public", 404);

        var response = await host.CreateClient().GetAsync("/Doctors/yok");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Signed_in_user_calls_the_detail_endpoint_with_token_and_sees_phone_and_email()
    {
        await using var host = await StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.Contains("0555 000 00 00", html);
        Assert.Contains("ali@hastane.test", html);
        Assert.DoesNotContain("Telefon ve e-posta bilgisi için giriş yapın", html);
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/doctors/d1").Single().Authorization);
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/doctors/d1/public"));
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    public async Task Other_signed_in_roles_also_use_the_detail_endpoint(string role)
    {
        await using var host = await StartAsync();
        var client = await host.LoginAsAsync(role);

        var response = await client.GetAsync("/Doctors/d1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(host.Gateway.Calls("GET", "/gateway/doctors/d1"));
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/doctors/d1/public"));
    }

    [Fact]
    public async Task Review_write_actions_still_require_the_patient_role()
    {
        await using var host = await StartAsync();

        var anonymous = await host.PostAsync(host.CreateClient(), "/Doctors/d1/Reviews", new() { ["ReviewForm.Rating"] = "5" });

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/reviews"));
    }
}

public class ReviewOwnershipTests
{
    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync(string sessionName, params object[] reviews)
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        host.Gateway.MapJson("GET", "/gateway/reviews/doctors/d1/summary", 200, new { doctorId = "d1", reviewCount = reviews.Length, averageRating = 4.0, recommendationPercent = 100 });
        host.Gateway.MapJson("GET", "/gateway/reviews", 200, reviews);
        return (host, await host.LoginAsAsync("Patient", sessionName));
    }

    [Fact]
    public async Task Controls_follow_isMine_not_the_reviewer_name()
    {
        // Oturum adı "Ayşe Yılmaz": adı aynı olan r1 isMine=false (başkasının, aynı adlı yorum), adı farklı olan r2 isMine=true.
        var (host, client) = await StartAsync("Ayşe Yılmaz",
            Review("r1", "Ayşe Yılmaz", 5, "Aynı ad, başkasının", isMine: false),
            Review("r2", "Takma Ad", 4, "Benim yorumum", isMine: true));
        await using var _h = host;

        var html = await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("/Doctors/d1/Reviews/r1/", html);
        Assert.Contains("/Doctors/d1/Reviews/r2/Update", html);
        Assert.Contains("/Doctors/d1/Reviews/r2/Delete", html);
    }

    [Fact]
    public async Task No_review_is_mine_means_no_controls_even_with_matching_name()
    {
        var (host, client) = await StartAsync("Ayşe Yılmaz", Review("r1", "Ayşe Yılmaz", 5, "Harika"));
        await using var _h = host;

        var html = await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("/Reviews/r1/", html);
    }

    [Fact]
    public async Task Missing_isMine_field_defaults_to_not_mine()
    {
        var (host, client) = await StartAsync("Ayşe Yılmaz",
            new { id = "r1", doctorId = "d1", reviewerName = "Ayşe Yılmaz", rating = 5, comment = "Eski sözleşme", createdAt = DateTime.UtcNow });
        await using var _h = host;

        var html = await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.Contains("Eski sözleşme", html);
        Assert.DoesNotContain("/Reviews/r1/", html);
    }

    [Fact]
    public async Task Doctor_role_never_sees_review_controls_even_if_isMine()
    {
        var host = await WebUiTestHost.StartAsync();
        await using var _h = host;
        host.Gateway.MapBranches("Kardiyoloji");
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        host.Gateway.MapJson("GET", "/gateway/reviews/doctors/d1/summary", 200, new { doctorId = "d1", reviewCount = 1, averageRating = 4.0, recommendationPercent = 100 });
        host.Gateway.MapJson("GET", "/gateway/reviews", 200, new[] { Review("r1", "Dr", 5, "x", isMine: true) });
        var client = await host.LoginAsAsync("Doctor");

        var html = await (await client.GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("/Reviews/r1/", html);
    }
}

public class AccountSettingsTests
{
    private static object Me(string first = "Ayşe", string last = "Yılmaz") => new
    {
        id = "user-1",
        firstName = first,
        lastName = last,
        email = "ayse@test.local",
        roles = new[] { "Patient" },
    };

    public static TheoryData<string> Pages() => new() { "/Account/Profile", "/Account/ChangePassword" };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Anonymous_is_redirected_to_login_without_calling_the_gateway(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login?returnUrl=", Location(response));
        Assert.Empty(host.Gateway.Requests);
    }

    [Theory]
    [InlineData("/Account/Profile")]
    [InlineData("/Account/ChangePassword")]
    public async Task Anonymous_post_never_reaches_the_gateway(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.PostAsync(host.CreateClient(), path, new() { ["FirstName"] = "A", ["LastName"] = "B", ["CurrentPassword"] = "x", ["NewPassword"] = "Yeni12345", ["ConfirmPassword"] = "Yeni12345" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", Location(response));
        Assert.Empty(host.Gateway.Calls("PUT", "/gateway/auth/me"));
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/auth/change-password"));
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    public async Task Every_role_can_open_profile_and_password_pages(string role)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me", 200, new { id = "user-1", firstName = "Ayşe", lastName = "Yılmaz", email = "ayse@test.local", roles = new[] { role } });
        var client = await host.LoginAsAsync(role);

        var profile = await client.GetAsync("/Account/Profile");
        var password = await client.GetAsync("/Account/ChangePassword");

        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal(HttpStatusCode.OK, password.StatusCode);
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/auth/me").First().Authorization);
    }

    [Fact]
    public async Task Profile_shows_names_editable_and_email_and_roles_read_only()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me", 200, new { id = "user-1", firstName = "Ayşe", lastName = "Yılmaz", email = "ayse@test.local", roles = new[] { "Patient", "Doctor" } });
        var client = await host.LoginAsAsync("Patient");

        var html = await (await client.GetAsync("/Account/Profile")).Content.ReadHtmlAsync();

        Assert.Contains("value=\"Ayşe\"", html);
        Assert.Contains("value=\"Yılmaz\"", html);
        Assert.Contains("value=\"ayse@test.local\" readonly", html);
        Assert.DoesNotContain("name=\"Email\"", html); // e-posta formdan gönderilmez
        Assert.Contains("Hasta", html);
        Assert.Contains("Doktor", html);
    }

    [Fact]
    public async Task Profile_post_puts_trimmed_names_only_and_tells_the_user_the_menu_name_refreshes_on_next_login()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me", 200, Me());
        host.Gateway.MapJson("PUT", "/gateway/auth/me", 200, Me("Yeni", "Soyad"));
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostFormAsync(client, "/Account/Profile", new()
        {
            ["FirstName"] = "  Yeni ",
            ["LastName"] = "Soyad",
            ["Email"] = "kotu@test.local",
            ["Roles"] = "Admin",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Profile", Location(response));
        var sent = host.Gateway.Calls("PUT", "/gateway/auth/me").Single();
        Assert.StartsWith("Bearer ", sent.Authorization);
        var json = Json(sent.Body);
        Assert.Equal("Yeni", json.GetProperty("firstName").GetString());
        Assert.Equal("Soyad", json.GetProperty("lastName").GetString());
        Assert.False(json.TryGetProperty("email", out _));
        Assert.False(json.TryGetProperty("roles", out _));
        // oturum kapanmaz: aynı istemciyle sayfa hâlâ açılır ve bilgi mesajı görünür
        var next = await (await client.GetAsync("/Account/Profile")).Content.ReadHtmlAsync();
        Assert.Contains("Bilgileriniz güncellendi", next);
        Assert.Contains("bir sonraki girişinizde", next);
    }

    [Fact]
    public async Task Profile_post_api_400_is_shown_in_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me", 200, Me());
        host.Gateway.MapJson("PUT", "/gateway/auth/me", 400, Problem(400, "Geçersiz", new { FirstName = new[] { "Ad geçersiz." } }));
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostFormAsync(client, "/Account/Profile", new() { ["FirstName"] = "X", ["LastName"] = "Y" });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ad geçersiz.", html);
        Assert.Contains("value=\"ayse@test.local\"", html); // salt okunur alanlar API'den yeniden doldurulur
    }

    [Fact]
    public async Task Profile_post_with_empty_or_too_long_names_never_reaches_the_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me", 200, Me());
        var client = await host.LoginAsAsync("Patient");

        var empty = await (await host.PostFormAsync(client, "/Account/Profile", new() { ["FirstName"] = "", ["LastName"] = "" })).Content.ReadHtmlAsync();
        var longName = await (await host.PostFormAsync(client, "/Account/Profile", new() { ["FirstName"] = new string('a', 101), ["LastName"] = "Y" })).Content.ReadHtmlAsync();

        Assert.Contains("Ad gerekli.", empty);
        Assert.Contains("Soyad gerekli.", empty);
        Assert.Contains("Ad en çok 100 karakter olabilir.", longName);
        Assert.Empty(host.Gateway.Calls("PUT", "/gateway/auth/me"));
    }

    [Fact]
    public async Task Profile_post_without_antiforgery_token_is_rejected()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var response = await client.PostAsync("/Account/Profile", new FormUrlEncodedContent(new Dictionary<string, string> { ["FirstName"] = "A", ["LastName"] = "B" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("PUT", "/gateway/auth/me"));
    }

    [Fact]
    public async Task Password_change_success_posts_both_passwords_signs_out_and_sends_to_login_with_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me", 200, Me());
        host.Gateway.MapStatus("POST", "/gateway/auth/change-password", 204);
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostFormAsync(client, "/Account/ChangePassword", new()
        {
            ["CurrentPassword"] = "Eski12345",
            ["NewPassword"] = "Yeni12345",
            ["ConfirmPassword"] = "Yeni12345",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", Location(response));
        var sent = host.Gateway.Calls("POST", "/gateway/auth/change-password").Single();
        Assert.StartsWith("Bearer ", sent.Authorization);
        var json = Json(sent.Body);
        Assert.Equal("Eski12345", json.GetProperty("currentPassword").GetString());
        Assert.Equal("Yeni12345", json.GetProperty("newPassword").GetString());
        Assert.False(json.TryGetProperty("confirmPassword", out _));
        // oturum çerezi silindi: korumalı sayfa artık girişe yönlendirir
        Assert.Contains("expires=Thu, 01 Jan 1970", WebUiTestHost.AuthSetCookie(response));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Account/Profile")).StatusCode);
        var login = await (await client.GetAsync("/Account/Login")).Content.ReadHtmlAsync();
        Assert.Contains("Şifre değişti, yeniden giriş yapın.", login);
    }

    [Fact]
    public async Task Password_change_400_wrong_current_password_is_shown_and_session_stays_open()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me", 200, Me());
        host.Gateway.MapJson("POST", "/gateway/auth/change-password", 400, Problem(400, "Mevcut şifre hatalı."));
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostFormAsync(client, "/Account/ChangePassword", new()
        {
            ["CurrentPassword"] = "yanlis",
            ["NewPassword"] = "Yeni12345",
            ["ConfirmPassword"] = "Yeni12345",
        });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Mevcut şifre hatalı.", html);
        Assert.DoesNotContain("value=\"Yeni12345\"", html); // şifreler geri yazılmaz
        Assert.Null(WebUiTestHost.AuthSetCookie(response));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Profile")).StatusCode);
    }

    [Fact]
    public async Task Password_change_400_rule_errors_are_shown_on_the_field()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/auth/change-password", 400,
            Problem(400, "Geçersiz", new { NewPassword = new[] { "Yeni şifre mevcut şifreyle aynı olamaz." } }));
        var client = await host.LoginAsAsync("Patient");

        var html = await (await host.PostFormAsync(client, "/Account/ChangePassword", new()
        {
            ["CurrentPassword"] = "Ayni12345",
            ["NewPassword"] = "Ayni12345",
            ["ConfirmPassword"] = "Ayni12345",
        })).Content.ReadHtmlAsync();

        Assert.Contains("Yeni şifre mevcut şifreyle aynı olamaz.", html);
    }

    [Fact]
    public async Task Password_change_423_shows_account_locked_and_keeps_the_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/auth/change-password", 423, Problem(423, "Çok fazla başarısız deneme."));
        var client = await host.LoginAsAsync("Patient");

        var response = await host.PostFormAsync(client, "/Account/ChangePassword", new()
        {
            ["CurrentPassword"] = "x12345678",
            ["NewPassword"] = "Yeni12345",
            ["ConfirmPassword"] = "Yeni12345",
        });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hesap geçici olarak kilitlendi", html);
        Assert.Null(WebUiTestHost.AuthSetCookie(response));
    }

    [Fact]
    public async Task Password_change_with_mismatch_short_or_empty_values_never_reaches_the_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var mismatch = await (await host.PostFormAsync(client, "/Account/ChangePassword", new() { ["CurrentPassword"] = "Eski12345", ["NewPassword"] = "Yeni12345", ["ConfirmPassword"] = "Baska12345" })).Content.ReadHtmlAsync();
        var shortPw = await (await host.PostFormAsync(client, "/Account/ChangePassword", new() { ["CurrentPassword"] = "Eski12345", ["NewPassword"] = "Ab1", ["ConfirmPassword"] = "Ab1" })).Content.ReadHtmlAsync();
        var empty = await (await host.PostFormAsync(client, "/Account/ChangePassword", new())).Content.ReadHtmlAsync();

        Assert.Contains("Şifreler aynı değil.", mismatch);
        Assert.Contains("Şifre en az 8 karakter olmalı.", shortPw);
        Assert.Contains("Mevcut şifre gerekli.", empty);
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/auth/change-password"));
    }

    [Fact]
    public async Task Password_change_without_antiforgery_token_is_rejected()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync("Patient");

        var response = await client.PostAsync("/Account/ChangePassword", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CurrentPassword"] = "Eski12345",
            ["NewPassword"] = "Yeni12345",
            ["ConfirmPassword"] = "Yeni12345",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/auth/change-password"));
    }

    [Theory]
    [InlineData("Patient", "/Patient/Appointments")]
    [InlineData("Doctor", "/Doctor/Appointments")]
    [InlineData("Admin", "/Admin")]
    public async Task User_menu_and_panel_side_menu_link_to_account_settings(string role, string panel)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/auth/me", 200, Me());
        // panel sayfalarının listeleri boş: yalnız menü iskeleti sınanır
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", "/gateway/branches", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", "/gateway/users", 200, Array.Empty<object>());
        var client = host.CreateClient();
        var token = role == "Doctor" ? TestJwt.Create("user-1", ["Doctor"], doctorId: "doc-1") : WebUiTestHost.NewToken(role);
        await host.LoginAsync(client, token, roles: [role]);

        var panelHtml = await (await client.GetAsync(panel)).Content.ReadHtmlAsync();
        var homeHtml = await (await client.GetAsync("/Account/Profile")).Content.ReadHtmlAsync();

        foreach (var html in new[] { panelHtml, homeHtml })
        {
            Assert.Contains("href=\"/Account/Profile\"", html);
            Assert.Contains("href=\"/Account/ChangePassword\"", html);
        }

        Assert.Contains("Hesap ayarları", panelHtml);
    }

    [Fact]
    public async Task Login_and_register_pages_are_unchanged_and_still_anonymous()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = host.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Register")).StatusCode);
    }
}

public class AdminUsersListTests
{
    private static object UserRow(string id, string first, string last, string[] roles, string? doctorId = null)
        => new { id, firstName = first, lastName = last, email = $"{id}@test.local", roles, doctorId };

    private static void MapRoles(WebUiTestHost host)
        => host.Gateway.MapJson("GET", "/gateway/roles", 200, new[] { new { name = "Admin" }, new { name = "Doctor" }, new { name = "Patient" } });

    [Fact]
    public async Task Without_filters_asks_page_one_with_page_size_twenty_and_no_empty_params()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapRoles(host);
        var users = host.Gateway.MapLogged("GET", "/gateway/users", 200, new { items = new[] { UserRow("u1", "Ali", "Veli", ["Patient"]) }, totalCount = 1, page = 1, pageSize = 20 });
        var client = await AdminH.AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("?page=1&pageSize=20", users.LastQuery);
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/users").Last().Authorization);
    }

    [Fact]
    public async Task Search_role_and_page_are_forwarded_and_kept_in_the_form_and_pager()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapRoles(host);
        var users = host.Gateway.MapLogged("GET", "/gateway/users", 200, new
        {
            items = new[] { UserRow("u21", "Ali", "Kaya", ["Doctor"], "doc1") },
            totalCount = 45,
            page = 2,
            pageSize = 20,
        });
        var client = await AdminH.AdminClientAsync(host);

        var html = await (await client.GetAsync("/Admin/Users?search=ali%20kaya&role=Doctor&page=2")).Content.ReadHtmlAsync();

        Assert.Equal("?page=2&pageSize=20&search=ali%20kaya&role=Doctor", users.LastQuery);
        Assert.Contains("value=\"ali kaya\"", html);
        Assert.Contains("<option value=\"Doctor\" selected", html);
        Assert.Contains("2 / 3", html); // 45 kayıt, sayfa başı 20
        Assert.Contains("page=3", html);
        Assert.Contains("page=1", html);
        Assert.Contains("search=ali", html); // süzgeç sayfalamada korunur
        Assert.Contains("role=Doctor", html);
        Assert.Contains("Toplam 45 kullanıcı", html);
    }

    [Fact]
    public async Task Rows_show_name_email_roles_and_link_to_details()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapRoles(host);
        host.Gateway.MapJson("GET", "/gateway/users", 200, new
        {
            items = new[] { UserRow("u1", "Ali", "Veli", ["Patient"]), UserRow("u2", "Zeynep", "Kaya", ["Doctor", "Patient"], "doc1") },
            totalCount = 2,
            page = 1,
            pageSize = 20,
        });
        var client = await AdminH.AdminClientAsync(host);

        var html = await (await client.GetAsync("/Admin/Users")).Content.ReadHtmlAsync();

        Assert.Contains("Ali Veli", html);
        Assert.Contains("u2@test.local", html);
        Assert.Contains("href=\"/Admin/Users/Details/u1\"", html);
        Assert.Contains("href=\"/Admin/Users/Details/u2\"", html);
        Assert.DoesNotContain("page-link", html); // tek sayfa: sayfalama yok
    }

    [Fact]
    public async Task Empty_result_shows_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapRoles(host);
        host.Gateway.MapJson("GET", "/gateway/users", 200, new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 20 });
        var client = await AdminH.AdminClientAsync(host);

        var html = await (await client.GetAsync("/Admin/Users?search=yok")).Content.ReadHtmlAsync();

        Assert.Contains("Kullanıcı bulunamadı", html);
    }

    [Fact]
    public async Task Role_dropdown_failure_does_not_break_the_list()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/roles", 503);
        host.Gateway.MapJson("GET", "/gateway/users", 200, new { items = new[] { UserRow("u1", "Ali", "Veli", ["Patient"]) }, totalCount = 1, page = 1, pageSize = 20 });
        var client = await AdminH.AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ali Veli", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Api_400_on_the_list_is_shown_with_an_empty_table()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapRoles(host);
        host.Gateway.MapJson("GET", "/gateway/users", 400, Problem(400, "Geçersiz", new { page = new[] { "page 1 ile 10000 arasında olmalı." } }));
        var client = await AdminH.AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Users?page=99999");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("page 1 ile 10000 arasında olmalı.", html);
        Assert.Contains("Kullanıcı bulunamadı", html);
    }

    [Fact]
    public async Task Overlong_search_is_truncated_to_the_api_limit()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapRoles(host);
        var users = host.Gateway.MapLogged("GET", "/gateway/users", 200, new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 20 });
        var client = await AdminH.AdminClientAsync(host);

        await client.GetAsync("/Admin/Users?search=" + new string('a', 150));

        Assert.Equal("?page=1&pageSize=20&search=" + new string('a', 100), users.LastQuery);
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("Doctor")]
    public async Task Other_roles_cannot_list_users(string role)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await host.LoginAsAsync(role);

        var response = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/users"));
    }

    [Fact]
    public async Task Admin_home_shows_user_count_tile_and_asks_for_a_single_row()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var users = AdminH.MapCapturingQuery(host, "GET", "/gateway/users", 200, new { items = Array.Empty<object>(), totalCount = 37, page = 1, pageSize = 1 });
        var client = await AdminH.AdminClientAsync(host);

        var html = await (await client.GetAsync("/Admin")).Content.ReadHtmlAsync();

        Assert.Equal("?page=1&pageSize=1", users());
        Assert.Contains("Kullanıcı sayısı", html);
        Assert.Contains("st-value\">37<", html);
    }

    // ---- doktor bağını kaldır ----

    private static void MapUserPage(WebUiTestHost host, object user)
    {
        host.Gateway.MapJson("GET", "/gateway/users/user-9", 200, user);
        MapRoles(host);
        host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { AdminH.DoctorListItem() });
    }

    [Fact]
    public async Task Details_with_doctor_link_offers_unlink_and_without_link_does_not()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUserPage(host, AdminH.User(["Doctor"], AdminH.DoctorId));
        var client = await AdminH.AdminClientAsync(host);
        var linked = await (await client.GetAsync("/Admin/Users/Details/user-9")).Content.ReadHtmlAsync();

        MapUserPage(host, AdminH.User(["Doctor"]));
        var unlinked = await (await client.GetAsync("/Admin/Users/Details/user-9")).Content.ReadHtmlAsync();

        Assert.Contains("Bağı kaldır", linked);
        Assert.Contains("/Admin/Users/UnlinkDoctor/user-9", linked);
        Assert.Contains("confirm(", linked); // onay penceresi
        Assert.DoesNotContain("Bağı kaldır", unlinked);
    }

    [Fact]
    public async Task Unlink_post_sends_delete_to_doctor_link_and_reports_success()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUserPage(host, AdminH.User(["Doctor"], AdminH.DoctorId));
        host.Gateway.MapStatus("DELETE", "/gateway/users/user-9/doctor-link", 204);
        var client = await AdminH.AdminClientAsync(host);

        var response = await AdminH.PostAsync(host, client, "/Admin/Users/Details/user-9", "/Admin/Users/UnlinkDoctor/user-9");
        var next = await (await client.GetAsync(AdminH.Location(response)!)).Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Users/Details/user-9", AdminH.Location(response));
        var sent = host.Gateway.Calls("DELETE", "/gateway/users/user-9/doctor-link").Single();
        Assert.StartsWith("Bearer ", sent.Authorization);
        Assert.Contains("Doktor bağı kaldırıldı.", next);
    }

    [Fact]
    public async Task Unlink_unknown_user_404_message_is_shown()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUserPage(host, AdminH.User(["Doctor"], AdminH.DoctorId));
        host.Gateway.MapJson("DELETE", "/gateway/users/user-9/doctor-link", 404, Problem(404, "Kullanıcı bulunamadı."));
        var client = await AdminH.AdminClientAsync(host);

        var response = await AdminH.PostAsync(host, client, "/Admin/Users/Details/user-9", "/Admin/Users/UnlinkDoctor/user-9");
        var next = await (await client.GetAsync(AdminH.Location(response)!)).Content.ReadHtmlAsync();

        Assert.Contains("Kullanıcı bulunamadı.", next);
    }

    [Fact]
    public async Task Unlink_cannot_be_done_with_get()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("DELETE", "/gateway/users/user-9/doctor-link", 204);
        var client = await AdminH.AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Users/UnlinkDoctor/user-9");

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"GET beklenmedik yanıt: {response.StatusCode}");
        Assert.Empty(host.Gateway.Calls("DELETE", "/gateway/users/user-9/doctor-link"));
    }

    [Fact]
    public async Task Unlink_requires_antiforgery_token_and_admin_role()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("DELETE", "/gateway/users/user-9/doctor-link", 204);
        var admin = await AdminH.AdminClientAsync(host);
        var doctor = await host.LoginAsAsync("Doctor");

        var noToken = await admin.PostAsync("/Admin/Users/UnlinkDoctor/user-9", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var wrongRole = await host.PostAsync(doctor, "/Admin/Users/UnlinkDoctor/user-9");
        var anonymous = await host.PostAsync(host.CreateClient(), "/Admin/Users/UnlinkDoctor/user-9");

        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Empty(host.Gateway.Calls("DELETE", "/gateway/users/user-9/doctor-link"));
    }
}
