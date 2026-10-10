using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Hastane.TestSupport;

namespace Hastane.WebUI.Tests;

// Doktor paneli: Çalışma Saatleri, Sosyal Medya, Faturalarım, Hasta Profili. Sahte gateway deseni (UI-KURALLARI §7).
public class DoctorExtraScreensTests
{
    private const string DoctorId = "doc-1";
    private const string PatientA = "11111111-1111-1111-1111-111111111111";
    private static readonly Guid Appt1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Appt2 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid Rx1 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Inv1 = Guid.Parse("55555555-5555-5555-5555-555555555551");
    private static readonly Guid Inv2 = Guid.Parse("55555555-5555-5555-5555-555555555552");
    private static readonly Guid Pay1 = Guid.Parse("44444444-4444-4444-4444-444444444441");
    private static readonly Guid Pay2 = Guid.Parse("44444444-4444-4444-4444-444444444442");

    private static async Task<HttpClient> DoctorClientAsync(WebUiTestHost host)
    {
        var client = host.CreateClient();
        await host.LoginAsync(client, TestJwt.Create("user-1", ["Doctor"], doctorId: DoctorId), roles: ["Doctor"]);
        return client;
    }

    // Tüm alanları dolu bir doktor kaydı: "diğer alanlar aynen korunur" iddiasını sınamak için.
    private static object FullDoctor() => new
    {
        id = DoctorId,
        firstName = "Ali",
        lastName = "Veli",
        title = "Dr.",
        gender = "Male",
        degrees = "Kardiyoloji Uzmanı",
        branchId = "b0",
        biography = "Kalp hastalıkları uzmanı.",
        imageUrl = "/img/doctors/avatar-01.svg",
        phone = "0555 000 00 00",
        email = "ali@hastane.test",
        services = new[] { "EKG", "Efor testi" },
        gallery = new[] { "https://example.com/k1.jpg" },
        socialLinks = new { facebook = "https://facebook.com/ali", x = (string?)null, instagram = "https://instagram.com/ali", linkedIn = (string?)null, pinterest = (string?)null, youTube = (string?)null },
        educations = new[] { new { school = "Ankara Üniversitesi", department = "Tıp", startYear = 2000, endYear = 2006 } },
        experiences = new[] { new { place = "Şehir Hastanesi", position = "Uzman", startYear = 2010, endYear = (int?)null } },
        awards = new[] { new { title = "Yılın Doktoru", year = 2020, description = "Ödül" } },
        clinicLocations = new object[]
        {
            new
            {
                clinicName = "Merkez Klinik", city = "Ankara", district = "Çankaya", address = "Atatürk Bulvarı 1", floor = "3",
                hourlyFee = (decimal?)750m, currency = "TRY",
                schedule = new[] { new { dayOfWeek = "Monday", startTime = "09:00", endTime = "12:00", slotMinutes = 30 } },
            },
            new
            {
                clinicName = "Özel Klinik", city = "İzmir", district = (string?)null, address = (string?)null, floor = (string?)null,
                hourlyFee = (decimal?)null, currency = "TRY",
                schedule = new[] { new { dayOfWeek = "Friday", startTime = "13:00", endTime = "17:00", slotMinutes = 20 } },
            },
        },
    };

    private static void MapDoctor(WebUiTestHost host)
    {
        host.Gateway.MapJson("GET", $"/gateway/doctors/{DoctorId}", 200, FullDoctor());
        host.Gateway.MapStatus("PUT", $"/gateway/doctors/{DoctorId}", 204);
        host.Gateway.MapBranches("Kardiyoloji");
    }

    private static JsonElement PutBody(WebUiTestHost host)
        => PublicPatientTestHelpers.Json(host.Gateway.Calls("PUT", $"/gateway/doctors/{DoctorId}").Single().Body);

    private static Dictionary<string, string> ScheduleForm(string clinic0Slots, string? op = null)
    {
        var form = new Dictionary<string, string>
        {
            ["ClinicLocations[0].ClinicName"] = "Merkez Klinik",
            ["ClinicLocations[1].ClinicName"] = "Özel Klinik",
            ["ClinicLocations[1].Schedule[0].DayOfWeek"] = "Friday",
            ["ClinicLocations[1].Schedule[0].StartTime"] = "13:00",
            ["ClinicLocations[1].Schedule[0].EndTime"] = "17:00",
            ["ClinicLocations[1].Schedule[0].SlotMinutes"] = "20",
        };
        // clinic0Slots: "Tuesday|10:00:00|11:30|45;Monday|09:00|10:00|30" biçiminde satırlar.
        var i = 0;
        foreach (var slot in clinic0Slots.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = slot.Split('|');
            form[$"ClinicLocations[0].Schedule[{i}].DayOfWeek"] = p[0];
            form[$"ClinicLocations[0].Schedule[{i}].StartTime"] = p[1];
            form[$"ClinicLocations[0].Schedule[{i}].EndTime"] = p[2];
            form[$"ClinicLocations[0].Schedule[{i}].SlotMinutes"] = p[3];
            i++;
        }

        if (op is not null) form["op"] = op;
        return form;
    }

    // ---------- Çalışma Saatleri ----------

    [Fact]
    public async Task Schedule_page_lists_clinics_with_rows_and_reads_own_record_with_bearer()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Schedule")).Content.ReadHtmlAsync();

        Assert.Contains("Çalışma Saatleri", html);
        Assert.Contains("Merkez Klinik", html);
        Assert.Contains("Özel Klinik", html);
        Assert.Contains("value=\"09:00\"", html);
        Assert.Contains("value=\"13:00\"", html);
        Assert.Contains("add-slot:1", html); // JS'siz ekle düğmesi
        Assert.Contains("remove-slot:0:0", html);
        var call = host.Gateway.Calls("GET", $"/gateway/doctors/{DoctorId}").Single();
        Assert.StartsWith("Bearer ", call.Authorization);
        Assert.Contains("/Doctor/Schedule", html); // kenar menüde
    }

    [Fact]
    public async Task Schedule_post_puts_whole_record_keeping_other_fields_and_taking_id_from_token()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        var client = await DoctorClientAsync(host);
        var form = ScheduleForm("Tuesday|10:00:00|11:30|45;Monday|09:00|10:00|30");
        form["Id"] = "baska-doktor"; // sahte kimlik: yok sayılmalı
        form["doctorId"] = "baska-doktor";

        var response = await host.PostAsync(client, "/Doctor/Schedule", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Doctor/Schedule", PublicPatientTestHelpers.Location(response));
        Assert.Empty(host.Gateway.Calls("PUT", "/gateway/doctors/baska-doktor"));
        var put = host.Gateway.Calls("PUT", $"/gateway/doctors/{DoctorId}").Single();
        Assert.StartsWith("Bearer ", put.Authorization);
        var body = PublicPatientTestHelpers.Json(put.Body);
        Assert.Equal(DoctorId, body.GetProperty("id").GetString());
        // Schedule dışındaki HER alan korunur:
        Assert.Equal("Ali", body.GetProperty("firstName").GetString());
        Assert.Equal("Veli", body.GetProperty("lastName").GetString());
        Assert.Equal("Dr.", body.GetProperty("title").GetString());
        Assert.Equal("Male", body.GetProperty("gender").GetString());
        Assert.Equal("b0", body.GetProperty("branchId").GetString());
        Assert.Equal("Kalp hastalıkları uzmanı.", body.GetProperty("biography").GetString());
        Assert.Equal("0555 000 00 00", body.GetProperty("phone").GetString());
        Assert.Equal("ali@hastane.test", body.GetProperty("email").GetString());
        Assert.Equal(2, body.GetProperty("services").GetArrayLength());
        Assert.Equal("https://example.com/k1.jpg", body.GetProperty("gallery")[0].GetString());
        Assert.Equal("https://facebook.com/ali", body.GetProperty("socialLinks").GetProperty("facebook").GetString());
        Assert.Equal("Ankara Üniversitesi", body.GetProperty("educations")[0].GetProperty("school").GetString());
        Assert.Equal("Yılın Doktoru", body.GetProperty("awards")[0].GetProperty("title").GetString());
        var clinics = body.GetProperty("clinicLocations");
        Assert.Equal(2, clinics.GetArrayLength());
        Assert.Equal("Atatürk Bulvarı 1", clinics[0].GetProperty("address").GetString());
        Assert.Equal(750m, clinics[0].GetProperty("hourlyFee").GetDecimal());
        // Yalnız klinik 0'ın programı değişti ("10:00:00" -> "10:00"); klinik 1 aynı.
        var s0 = clinics[0].GetProperty("schedule");
        Assert.Equal(2, s0.GetArrayLength());
        Assert.Equal("Tuesday", s0[0].GetProperty("dayOfWeek").GetString());
        Assert.Equal("10:00", s0[0].GetProperty("startTime").GetString());
        Assert.Equal("11:30", s0[0].GetProperty("endTime").GetString());
        Assert.Equal(45, s0[0].GetProperty("slotMinutes").GetInt32());
        Assert.Equal("Friday", clinics[1].GetProperty("schedule")[0].GetProperty("dayOfWeek").GetString());
    }

    [Fact]
    public async Task Schedule_add_and_remove_row_buttons_work_without_saving()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        var client = await DoctorClientAsync(host);

        var added = await host.PostAsync(client, "/Doctor/Schedule", ScheduleForm("Monday|09:00|12:00|30", op: "add-slot:0"));
        var addedHtml = await added.Content.ReadHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Contains("remove-slot:0:1", addedHtml); // ikinci satır geldi

        var removed = await host.PostAsync(client, "/Doctor/Schedule", ScheduleForm("Monday|09:00|12:00|30", op: "remove-slot:0:0"));
        var removedHtml = await removed.Content.ReadHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.DoesNotContain("remove-slot:0:0", removedHtml);
        Assert.Contains("henüz saat aralığı yok", removedHtml);
        Assert.Empty(host.Gateway.Calls("PUT", $"/gateway/doctors/{DoctorId}"));
    }

    [Fact]
    public async Task Schedule_row_errors_are_shown_on_the_rows_and_nothing_is_saved()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        var client = await DoctorClientAsync(host);

        var endBeforeStart = await (await host.PostAsync(client, "/Doctor/Schedule", ScheduleForm("Monday|12:00|09:00|30"))).Content.ReadHtmlAsync();
        Assert.Contains("Bitiş, başlangıçtan sonra olmalı.", endBeforeStart);

        var overlap = await (await host.PostAsync(client, "/Doctor/Schedule", ScheduleForm("Monday|09:00|12:00|30;Monday|11:00|13:00|30"))).Content.ReadHtmlAsync();
        Assert.Contains("ile çakışıyor", overlap);

        var badMinutes = await (await host.PostAsync(client, "/Doctor/Schedule", ScheduleForm("Monday|09:00|12:00|3"))).Content.ReadHtmlAsync();
        Assert.Contains("Randevu süresi 5-120 dakika olmalı.", badMinutes);

        var missingTime = await (await host.PostAsync(client, "/Doctor/Schedule", ScheduleForm("Monday||12:00|30"))).Content.ReadHtmlAsync();
        Assert.Contains("Başlangıç saati gerekli.", missingTime);

        Assert.Empty(host.Gateway.Calls("PUT", $"/gateway/doctors/{DoctorId}"));
    }

    [Fact]
    public async Task Schedule_api_400_is_shown_on_the_clinic()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        host.Gateway.MapJson("PUT", $"/gateway/doctors/{DoctorId}", 400, new
        {
            status = 400,
            title = "Doğrulama hatası",
            errors = new Dictionary<string, string[]> { ["ClinicLocations[0].Schedule"] = ["Schedule servis tarafından reddedildi."] },
        });
        var client = await DoctorClientAsync(host);

        var response = await host.PostAsync(client, "/Doctor/Schedule", ScheduleForm("Monday|09:00|12:00|30"));
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Schedule servis tarafından reddedildi.", html);
    }

    [Fact]
    public async Task Schedule_post_when_clinic_list_changed_does_not_write_to_the_wrong_clinic()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        var client = await DoctorClientAsync(host);
        var form = ScheduleForm("Monday|09:00|12:00|30");
        form["ClinicLocations[0].ClinicName"] = "Eski Klinik Adı";

        var response = await host.PostAsync(client, "/Doctor/Schedule", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("PUT", $"/gateway/doctors/{DoctorId}"));
    }

    [Theory]
    [InlineData("/Doctor/Schedule")]
    [InlineData("/Doctor/Social")]
    [InlineData("/Doctor/Invoices")]
    [InlineData("/Doctor/Patients/11111111-1111-1111-1111-111111111111")]
    public async Task New_doctor_screens_reject_other_roles_and_anonymous(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await host.CreateClient().GetAsync(path)).StatusCode);
        foreach (var role in new[] { "Patient", "Admin" })
        {
            var client = await host.LoginAsAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }

        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/doctors/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/Doctor/Schedule")]
    [InlineData("/Doctor/Social")]
    public async Task Save_posts_require_antiforgery_and_the_doctor_role(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        var doctor = await DoctorClientAsync(host);

        var noToken = await doctor.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["ClinicLocations[0].ClinicName"] = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        var patient = await host.LoginAsAsync("Patient");
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(patient, path)).StatusCode);

        Assert.Empty(host.Gateway.Calls("PUT", $"/gateway/doctors/{DoctorId}"));
    }

    // ---------- Sosyal Medya ----------

    [Fact]
    public async Task Social_page_shows_current_links()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Social")).Content.ReadHtmlAsync();

        Assert.Contains("Sosyal Medya", html);
        Assert.Contains("https://facebook.com/ali", html);
        Assert.Contains("https://instagram.com/ali", html);
        Assert.Contains("name=\"SocialLinks.YouTube\"", html);
    }

    [Fact]
    public async Task Social_post_puts_whole_record_with_only_links_changed_and_id_from_token()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        var client = await DoctorClientAsync(host);

        var response = await host.PostAsync(client, "/Doctor/Social", new Dictionary<string, string>
        {
            ["SocialLinks.Facebook"] = "",
            ["SocialLinks.X"] = " https://x.com/ali ",
            ["SocialLinks.Instagram"] = "https://instagram.com/ali",
            ["SocialLinks.LinkedIn"] = "https://linkedin.com/in/ali",
            ["SocialLinks.YouTube"] = "http://youtube.com/@ali",
            ["Id"] = "baska-doktor",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var body = PutBody(host);
        Assert.Equal(DoctorId, body.GetProperty("id").GetString());
        var links = body.GetProperty("socialLinks");
        Assert.Equal(JsonValueKind.Null, links.GetProperty("facebook").ValueKind); // boş = kaldırıldı
        Assert.Equal("https://x.com/ali", links.GetProperty("x").GetString());
        Assert.Equal("https://linkedin.com/in/ali", links.GetProperty("linkedIn").GetString());
        Assert.Equal("http://youtube.com/@ali", links.GetProperty("youTube").GetString());
        // diğer alanlar aynen:
        Assert.Equal("Kalp hastalıkları uzmanı.", body.GetProperty("biography").GetString());
        Assert.Equal(2, body.GetProperty("clinicLocations").GetArrayLength());
        Assert.Equal("Friday", body.GetProperty("clinicLocations")[1].GetProperty("schedule")[0].GetProperty("dayOfWeek").GetString());
        Assert.Equal("0555 000 00 00", body.GetProperty("phone").GetString());
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("ftp://example.com/x")]
    [InlineData("example.com/ali")]
    public async Task Social_post_rejects_non_http_addresses(string value)
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        var client = await DoctorClientAsync(host);

        var response = await host.PostAsync(client, "/Doctor/Social", new Dictionary<string, string> { ["SocialLinks.Instagram"] = value });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Geçerli bir http:// ya da https:// adresi yazın.", html);
        Assert.Empty(host.Gateway.Calls("PUT", $"/gateway/doctors/{DoctorId}"));
    }

    [Fact]
    public async Task Social_api_400_is_shown_on_the_field()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        host.Gateway.MapJson("PUT", $"/gateway/doctors/{DoctorId}", 400, new
        {
            status = 400,
            title = "Doğrulama hatası",
            errors = new Dictionary<string, string[]> { ["SocialLinks.Facebook"] = ["Servis bu adresi reddetti."] },
        });
        var client = await DoctorClientAsync(host);

        var response = await host.PostAsync(client, "/Doctor/Social", new Dictionary<string, string> { ["SocialLinks.Facebook"] = "https://facebook.com/ali" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Servis bu adresi reddetti.", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Public_profile_shows_only_http_social_links_with_safe_rel_and_target()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapBranches("Kardiyoloji");
        var doctor = new
        {
            id = "d1",
            firstName = "Ali",
            lastName = "Veli",
            title = "Dr.",
            gender = "Male",
            branchId = "b0",
            services = Array.Empty<string>(),
            gallery = Array.Empty<string>(),
            socialLinks = new
            {
                facebook = "https://facebook.com/ali",
                x = "javascript:alert(document.cookie)",
                instagram = "data:text/html,<script>alert(1)</script>",
                linkedIn = "http://linkedin.com/in/ali",
                pinterest = (string?)null,
                youTube = "/etc/passwd",
            },
            educations = Array.Empty<object>(),
            experiences = Array.Empty<object>(),
            awards = Array.Empty<object>(),
            clinicLocations = Array.Empty<object>(),
        };
        host.Gateway.MapJson("GET", "/gateway/doctors/d1/public", 200, doctor);
        host.Gateway.MapJson("GET", "/gateway/reviews/doctors/d1/summary", 200, new { doctorId = "d1", reviewCount = 0, averageRating = 0.0, recommendationPercent = 0 });
        host.Gateway.MapJson("GET", "/gateway/reviews", 200, Array.Empty<object>());

        var html = await (await host.CreateClient().GetAsync("/Doctors/d1")).Content.ReadHtmlAsync();

        Assert.Contains("href=\"https://facebook.com/ali\"", html);
        Assert.Contains("href=\"http://linkedin.com/in/ali\"", html);
        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html);
        Assert.Contains("bi-facebook", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("data:text/html", html);
        Assert.DoesNotContain("/etc/passwd", html);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "rel=\"noopener noreferrer nofollow\"").Count);
    }

    // ---------- Faturalarım ----------

    private static object InvoiceItem(Guid id, Guid paymentId, string number, decimal total, DateTime issued) => new
    {
        id,
        invoiceNumber = number,
        paymentId,
        patientId = PatientA,
        doctorId = DoctorId,
        issuedAt = issued,
        currency = "TRY",
        subtotal = total,
        taxPercent = 0m,
        taxAmount = 0m,
        total,
    };

    private static object PaymentItem(Guid id, string status, decimal amount, DateTime paidAt) => new
    {
        id,
        appointmentId = Appt1,
        doctorId = DoctorId,
        status,
        amount,
        currency = "TRY",
        createdAt = paidAt,
        paidAt,
    };

    // /gateway/payments yanıtını sorgudaki status değerine göre verir (sahte gateway yalnız yola bakar).
    private static ConcurrentQueue<string> MapPayments(WebUiTestHost host, object[] paid, object[] refunded, int status = 200)
    {
        var queries = new ConcurrentQueue<string>();
        host.Gateway.Map("GET", "/gateway/payments", ctx =>
        {
            queries.Enqueue(ctx.Request.QueryString.Value ?? string.Empty);
            ctx.Response.StatusCode = status;
            if (status != 200) return Task.CompletedTask;
            var items = ctx.Request.Query["status"] == "Paid" ? paid : refunded;
            return ctx.Response.WriteAsJsonAsync(new { items, totalCount = items.Length, page = 1, pageSize = 50 });
        });
        return queries;
    }

    private static void MapUsers(WebUiTestHost host)
        => host.Gateway.MapJson("GET", "/gateway/users", 200, new[] { new { id = PatientA, firstName = "Zeynep", lastName = "Kaya", email = "zeynep@test.local" } });

    [Fact]
    public async Task Invoices_list_shows_earnings_summary_refund_badge_and_never_sends_a_doctor_id()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUsers(host);
        var now = DateTime.UtcNow;
        var invoiceQueries = new ConcurrentQueue<string>();
        host.Gateway.Map("GET", "/gateway/invoices", ctx =>
        {
            invoiceQueries.Enqueue(ctx.Request.QueryString.Value ?? string.Empty);
            return ctx.Response.WriteAsJsonAsync(new
            {
                items = new[]
                {
                    InvoiceItem(Inv1, Pay1, "FTR-001", 510m, now),
                    InvoiceItem(Inv2, Pay2, "FTR-002", 310m, now.AddDays(-90)),
                },
                totalCount = 2,
                page = 1,
                pageSize = 10,
            });
        });
        // 2 ödenmiş (biri bu ay 500, biri eski 300) + 1 iade
        var queries = MapPayments(host,
            paid: [PaymentItem(Pay1, "Paid", 500m, now), PaymentItem(Guid.NewGuid(), "Paid", 300m, now.AddDays(-90))],
            refunded: [PaymentItem(Pay2, "Refunded", 300m, now.AddDays(-90))]);
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Invoices")).Content.ReadHtmlAsync();

        Assert.Contains("FTR-001", html);
        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains("Toplam kazanç", html);
        Assert.Contains("800,00 TRY", html);   // 500 + 300 (yalnız ödenmişler)
        Assert.Contains("500,00 TRY", html);   // bu ay
        Assert.Contains("Ödeme sayısı", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<span class=\"badge text-bg-secondary\">İade edildi</span>"));
        Assert.Contains($"/Doctor/Invoices/Details/{Inv1}", html);
        Assert.Equal("?page=1&pageSize=10", invoiceQueries.Single()); // DoctorId istemciden gitmez: kapsam token'dan
        Assert.All(queries, q => Assert.DoesNotContain("doctorId", q));
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/invoices").Single().Authorization);
    }

    [Fact]
    public async Task Invoices_list_still_opens_without_summary_when_payments_are_unavailable()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUsers(host);
        host.Gateway.MapJson("GET", "/gateway/invoices", 200, new { items = new[] { InvoiceItem(Inv1, Pay1, "FTR-001", 500m, DateTime.UtcNow) }, totalCount = 1, page = 1, pageSize = 10 });
        MapPayments(host, [], [], status: 503);
        var client = await DoctorClientAsync(host);

        var response = await client.GetAsync("/Doctor/Invoices");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("FTR-001", html);
        Assert.Contains("Kazanç özeti şu anda hesaplanamadı", html);
        Assert.DoesNotContain("Toplam kazanç", html); // tahmini sayı gösterilmez
    }

    [Fact]
    public async Task Invoices_empty_state_and_paging()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUsers(host);
        MapPayments(host, [], []);
        host.Gateway.MapJson("GET", "/gateway/invoices", 200, new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 10 });
        var client = await DoctorClientAsync(host);
        Assert.Contains("Henüz faturanız yok", await (await client.GetAsync("/Doctor/Invoices")).Content.ReadHtmlAsync());

        host.Gateway.MapJson("GET", "/gateway/invoices", 200, new { items = new[] { InvoiceItem(Inv1, Pay1, "FTR-021", 100m, DateTime.UtcNow) }, totalCount = 25, page = 3, pageSize = 10 });
        var html = await (await client.GetAsync("/Doctor/Invoices?page=3")).Content.ReadHtmlAsync();
        Assert.Contains("3 / 3", html);
    }

    [Fact]
    public async Task Invoice_details_shows_lines_and_refund_badge_and_print_page_has_no_site_menu()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUsers(host);
        var invoice = new
        {
            id = Inv2,
            invoiceNumber = "FTR-002",
            paymentId = Pay2,
            patientId = PatientA,
            doctorId = DoctorId,
            issuedAt = DateTime.UtcNow,
            currency = "TRY",
            subtotal = 300m,
            taxPercent = 10m,
            taxAmount = 30m,
            total = 330m,
            lines = new[] { new { description = "Muayene", quantity = 1, unitPrice = 300m, lineTotal = 300m } },
        };
        host.Gateway.MapJson("GET", $"/gateway/invoices/{Inv2}", 200, invoice);
        host.Gateway.MapJson("GET", $"/gateway/payments/{Pay2}", 200, PaymentItem(Pay2, "Refunded", 330m, DateTime.UtcNow));
        var client = await DoctorClientAsync(host);

        var details = await (await client.GetAsync($"/Doctor/Invoices/Details/{Inv2}")).Content.ReadHtmlAsync();
        Assert.Contains("FTR-002", details);
        Assert.Contains("Muayene", details);
        Assert.Contains("330,00 TRY", details);
        Assert.Contains("İade edildi", details);
        Assert.Contains($"/Doctor/Invoices/Print/{Inv2}", details);

        var print = await (await client.GetAsync($"/Doctor/Invoices/Print/{Inv2}")).Content.ReadHtmlAsync();
        Assert.Contains("print-sheet", print);
        Assert.Contains("window.print()", print);
        Assert.DoesNotContain("site-nav", print);
    }

    [Fact]
    public async Task Invoice_of_another_doctor_is_404()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", $"/gateway/invoices/{Inv1}", 404);
        var client = await DoctorClientAsync(host);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Doctor/Invoices/Details/{Inv1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Doctor/Invoices/Print/{Inv1}")).StatusCode);
    }

    // ---------- Hasta Profili ----------

    private static object Appt(Guid id, string status, DateTime date, string? note = null) => new
    {
        id,
        doctorId = DoctorId,
        patientId = PatientA,
        branchId = "b1",
        appointmentDate = date,
        status,
        createdAt = date.AddDays(-2),
        note,
    };

    [Fact]
    public async Task Patient_profile_shows_patient_appointments_notes_and_own_prescriptions()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUsers(host);
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { new { id = "b1", name = "Kardiyoloji" } });
        var apptQueries = new ConcurrentQueue<string>();
        host.Gateway.Map("GET", "/gateway/appointments", ctx =>
        {
            apptQueries.Enqueue(ctx.Request.QueryString.Value ?? string.Empty);
            return ctx.Response.WriteAsJsonAsync(new[]
            {
                Appt(Appt1, "Completed", DateTime.UtcNow.AddDays(-10), "Göğüs ağrısı şikayeti"),
                Appt(Appt2, "Pending", DateTime.UtcNow.AddDays(3)),
            });
        });
        var rxQueries = new ConcurrentQueue<string>();
        host.Gateway.Map("GET", "/gateway/prescriptions", ctx =>
        {
            rxQueries.Enqueue(ctx.Request.QueryString.Value ?? string.Empty);
            return ctx.Response.WriteAsJsonAsync(new[] { new { id = Rx1, appointmentId = Appt1, doctorId = DoctorId, patientId = PatientA, issuedAt = DateTime.UtcNow.AddDays(-9), itemCount = 3 } });
        });
        var client = await DoctorClientAsync(host);

        var response = await client.GetAsync($"/Doctor/Patients/{PatientA}");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains("zeynep@test.local", html);
        Assert.Contains("Göğüs ağrısı şikayeti", html);
        Assert.Contains("Kardiyoloji", html);
        Assert.Contains("Tamamlandı", html);
        Assert.Contains($"/Doctor/Appointments/Details/{Appt1}", html);
        Assert.Contains($"/Doctor/Prescriptions/Details/{Rx1}", html);
        Assert.Contains($"doctorId={DoctorId}", apptQueries.Single());
        Assert.Contains($"patientId={PatientA}", apptQueries.Single());
        Assert.Contains($"patientId={PatientA}", rxQueries.Single());
        Assert.Single(host.Gateway.Calls("GET", "/gateway/users")); // hasta adı/e-postası tek çağrı
        Assert.Contains("/Doctor/Patients", html);
    }

    [Fact]
    public async Task Patient_profile_without_any_appointment_with_this_doctor_is_404_and_leaks_no_user_data()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUsers(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, Array.Empty<object>());
        host.Gateway.MapJson("GET", "/gateway/prescriptions", 200, Array.Empty<object>());
        var client = await DoctorClientAsync(host);

        var response = await client.GetAsync("/Doctor/Patients/99999999-9999-9999-9999-999999999999");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Zeynep", html);
        Assert.Empty(host.Gateway.Calls("GET", "/gateway/users")); // kullanıcı kaydına hiç bakılmadı
    }

    [Fact]
    public async Task Patients_list_links_to_the_profile()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapUsers(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, new[] { Appt(Appt1, "Confirmed", DateTime.UtcNow.AddDays(1)) });
        var client = await DoctorClientAsync(host);

        var html = await (await client.GetAsync("/Doctor/Patients")).Content.ReadHtmlAsync();

        Assert.Contains($"href=\"/Doctor/Patients/{PatientA}\"", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Doctor/Patients/Index")).StatusCode);
    }

    // ---------- Kenar menü ve Profil bağlantıları ----------

    [Fact]
    public async Task Doctor_side_menu_has_the_four_new_pages_and_profile_points_to_them()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapDoctor(host);
        var client = await DoctorClientAsync(host);

        var profile = await (await client.GetAsync("/Doctor/Profile")).Content.ReadHtmlAsync();

        foreach (var path in new[] { "/Doctor/Schedule", "/Doctor/Invoices", "/Doctor/Patients", "/Doctor/Social" })
            Assert.Contains($"href=\"{path}\"", profile);
        Assert.Contains("Çalışma Saatleri", profile);
        Assert.Contains("panel-menu-toggle", profile); // mobilde açılır menü düğmesi
    }
}
