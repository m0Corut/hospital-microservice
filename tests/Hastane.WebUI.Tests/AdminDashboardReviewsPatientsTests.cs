using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Models.Api.Admin;
using Hastane.WebUI.ViewModels.Admin;
using static Hastane.WebUI.Tests.AdminPanelTestHelpers;

namespace Hastane.WebUI.Tests;

// Grafik toplama mantığı (saf birim testleri). Gün/ay gruplaması İstanbul takvimine göre, sunucu saat dilimine göre değil.
public class AdminDashboardBuilderTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc); // İstanbul: 10.10.2026 15:00

    private static AdminAppointmentApiModel Appt(DateTime utc, string status = "Confirmed", string doctorId = "d1", DateTime? created = null)
        => new() { Id = Guid.NewGuid(), DoctorId = doctorId, PatientId = "p", BranchId = "b1", AppointmentDate = DateTime.SpecifyKind(utc, DateTimeKind.Utc), Status = status, CreatedAt = created ?? utc };

    private static AdminPaymentApiModel Pay(DateTime? paidUtc, string status = "Paid", decimal amount = 100m, string currency = "TRY", DateTime? created = null)
        => new() { Id = Guid.NewGuid(), DoctorId = "d1", Status = status, Amount = amount, Currency = currency, PaidAt = paidUtc, CreatedAt = created ?? paidUtc ?? Now };

    [Fact]
    public void Daily_groups_by_istanbul_day_not_utc_day()
    {
        // 09.10 21:30 UTC = 10.10 00:30 İstanbul -> 10.10 gününe düşer; 09.10 20:59 UTC = 09.10 23:59 İstanbul.
        var series = AdminDashboardBuilder.BuildDaily(
            [Appt(new DateTime(2026, 10, 9, 21, 30, 0)), Appt(new DateTime(2026, 10, 9, 20, 59, 0)), Appt(new DateTime(2026, 10, 9, 21, 0, 0))], Now)!;

        Assert.Equal(30, series.Labels.Count);
        Assert.Equal("10.10", series.Labels[^1]);
        Assert.Equal("11.09", series.Labels[0]);
        Assert.Equal(2, series.Values[^1]);   // 10.10
        Assert.Equal(1, series.Values[^2]);   // 09.10
        Assert.Equal(3, series.Values.Sum());
    }

    [Fact]
    public void Daily_ignores_days_outside_the_window_and_fills_empty_days_with_zero()
    {
        var series = AdminDashboardBuilder.BuildDaily([Appt(new DateTime(2026, 8, 1, 9, 0, 0)), Appt(new DateTime(2026, 10, 20, 9, 0, 0))], Now)!;

        Assert.Equal(30, series.Values.Count);
        Assert.All(series.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Status_counts_known_statuses_in_fixed_order_and_skips_empty_ones()
    {
        var series = AdminDashboardBuilder.BuildStatus(
            [Appt(Now, "Cancelled"), Appt(Now, "Pending"), Appt(Now, "Pending"), Appt(Now, "Completed")])!;

        Assert.Equal(["Pending", "Completed", "Cancelled"], series.Keys);
        Assert.Equal([2, 1, 1], series.Values);
        Assert.Equal(["Beklemede", "Tamamlandı", "İptal edildi"], series.Labels);
    }

    [Fact]
    public void Revenue_counts_only_paid_payments_so_refunds_are_not_included()
    {
        var revenue = AdminDashboardBuilder.BuildRevenue(
        [
            Pay(new DateTime(2026, 10, 2, 9, 0, 0), amount: 300m),
            Pay(new DateTime(2026, 10, 3, 9, 0, 0), "Refunded", 999m), // iade edilmiş: Paid değil, toplama girmez
            Pay(null, "Failed", 50m),
            Pay(new DateTime(2026, 10, 4, 9, 0, 0), "Pending", 70m),
        ], Now, out var others)!;

        Assert.Equal(6, revenue.Labels.Count);
        Assert.Equal("Eki 2026", revenue.Labels[^1]);
        Assert.Equal("May 2026", revenue.Labels[0]);
        Assert.Equal(300m, revenue.Values[^1]);
        Assert.Equal(300m, revenue.Values.Sum());
        Assert.Empty(others);
    }

    [Fact]
    public void Revenue_groups_by_istanbul_month_and_separates_other_currencies()
    {
        // 30.09 21:30 UTC = 01.10 00:30 İstanbul -> Ekim; 30.09 20:59 UTC = 30.09 23:59 -> Eylül.
        var revenue = AdminDashboardBuilder.BuildRevenue(
        [
            Pay(new DateTime(2026, 9, 30, 21, 30, 0), amount: 10m),
            Pay(new DateTime(2026, 9, 30, 20, 59, 0), amount: 20m),
            Pay(new DateTime(2026, 10, 5, 9, 0, 0), amount: 5m, currency: "usd"),
            Pay(new DateTime(2026, 9, 6, 9, 0, 0), amount: 7m, currency: "USD"),
            Pay(new DateTime(2025, 12, 1, 9, 0, 0), amount: 1000m), // 6 ay penceresi dışı
        ], Now, out var others)!;

        Assert.Equal(20m, revenue.Values[^2]); // Eylül
        Assert.Equal(10m, revenue.Values[^1]); // Ekim
        var usd = Assert.Single(others);
        Assert.Equal(("USD", 12m), (usd.Currency, usd.Total));
    }

    [Fact]
    public void Revenue_uses_created_at_when_paid_at_is_missing()
    {
        var revenue = AdminDashboardBuilder.BuildRevenue([Pay(null, amount: 40m, created: new DateTime(2026, 10, 6, 9, 0, 0))], Now, out _)!;

        Assert.Equal(40m, revenue.Values[^1]);
    }

    [Fact]
    public void Doctors_by_branch_is_sorted_and_includes_branches_without_doctors()
    {
        var series = AdminDashboardBuilder.BuildDoctorsByBranch(
            [new() { Id = "1", BranchId = "b1" }, new() { Id = "2", BranchId = "b2" }, new() { Id = "3", BranchId = "b2" }],
            [new() { Id = "b1", Name = "Göz" }, new() { Id = "b2", Name = "Kalp" }, new() { Id = "b3", Name = "Cildiye" }])!;

        Assert.Equal(["Kalp", "Göz", "Cildiye"], series.Labels);
        Assert.Equal([2, 1, 0], series.Values);
    }

    [Fact]
    public void Missing_sources_give_null_charts_and_the_others_still_build()
    {
        var data = AdminDashboardBuilder.Build(new AdminDashboardRaw(null, [Pay(new DateTime(2026, 10, 2))], 1, null, null, Now));

        Assert.Null(data.Charts.Daily);
        Assert.Null(data.Charts.Status);
        Assert.Null(data.Charts.DoctorsByBranch);
        Assert.Null(data.RecentAppointments);
        Assert.NotNull(data.Charts.Revenue);
        Assert.Single(data.RecentPayments!);
    }

    [Fact]
    public void Recent_tables_hold_at_most_five_newest_rows_and_truncation_is_flagged_only_at_the_cap()
    {
        var payments = Enumerable.Range(0, AdminDashboardClient.MaxPayments)
            .Select(i => Pay(new DateTime(2026, 10, 1).AddMinutes(i), created: new DateTime(2026, 10, 1).AddMinutes(i))).ToList();
        var appts = Enumerable.Range(0, 8).Select(i => Appt(Now.AddDays(i), created: Now.AddHours(i))).ToList();

        var capped = AdminDashboardBuilder.Build(new AdminDashboardRaw(appts, payments, 900, [], [], Now));
        var small = AdminDashboardBuilder.Build(new AdminDashboardRaw(appts, payments.Take(3).ToList(), 3, [], [], Now));

        Assert.Equal(5, capped.RecentAppointments!.Count);
        Assert.Equal(5, capped.RecentPayments!.Count);
        Assert.True(capped.PaymentsTruncated);
        Assert.False(small.PaymentsTruncated);
    }
}

public class AdminDashboardPageTests
{
    private const string Evil = "</script><script>alert(1)</script>";

    private static void MapSummaryEndpoints(WebUiTestHost host)
    {
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, Paged([DoctorListItem()], total: 1));
        host.Gateway.MapJson("GET", "/gateway/invoices", 200, Paged([], total: 0));
        host.Gateway.MapJson("GET", "/gateway/users", 200, Paged([], total: 0));
    }

    private static string EmbeddedJson(string html)
    {
        var match = Regex.Match(html, "<script type=\"application/json\" id=\"dashboard-data\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(match.Success, "Grafik verisi application/json betiğinde olmalı.");
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task Dashboard_renders_all_charts_tables_and_loads_chartjs_locally()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSummaryEndpoints(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, new[] { Appointment() });
        host.Gateway.MapJson("GET", "/gateway/payments", 200, Paged([Payment()], pageSize: 50, total: 1));
        host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { DoctorListItem() });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { Branch() });
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var id in new[] { "chart-daily", "chart-status", "chart-revenue", "chart-doctors" })
            Assert.Contains($"id=\"{id}\"", html);
        Assert.Contains("/lib/chartjs/chart.umd.min.js", html);
        Assert.DoesNotContain("cdn.jsdelivr", html);
        Assert.Contains("Son randevular", html);
        Assert.Contains("Son ödemeler", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("450.50 TRY", html);
        Assert.DoesNotContain("Veri alınamadı", html);
        Assert.Contains("href=\"/Admin/Reviews\"", html);
        Assert.Contains("href=\"/Admin/Patients\"", html);
    }

    [Fact]
    public async Task Dashboard_asks_for_appointments_from_window_start_and_pages_payments_up_to_500()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSummaryEndpoints(host);
        var apptQueries = new ConcurrentQueue<string>();
        host.Gateway.Map("GET", "/gateway/appointments", ctx =>
        {
            apptQueries.Enqueue(ctx.Request.QueryString.Value ?? "");
            return ctx.Response.WriteAsJsonAsync(Array.Empty<object>());
        });
        var paymentQueries = new ConcurrentQueue<string>();
        host.Gateway.Map("GET", "/gateway/payments", ctx =>
        {
            paymentQueries.Enqueue(ctx.Request.QueryString.Value ?? "");
            var size = int.Parse(ctx.Request.Query["pageSize"].ToString());
            var items = Enumerable.Range(0, size).Select(_ => (object)Payment()).ToArray();
            return ctx.Response.WriteAsJsonAsync(Paged(items, pageSize: size, total: 900));
        });
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin"));

        var query = Assert.Single(apptQueries);
        Assert.Contains("from=", query);
        Assert.DoesNotContain("to=", query);
        Assert.Equal(10, paymentQueries.Count(q => q.Contains("pageSize=50"))); // 10 sayfa x 50 = üst sınır 500
        Assert.Contains("?page=10&pageSize=50", paymentQueries);
        Assert.DoesNotContain(paymentQueries, q => q.Contains("page=11"));
        Assert.Contains("son 500 ödeme", html);
    }

    [Fact]
    public async Task Dashboard_opens_with_data_unavailable_notes_when_every_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        foreach (var path in new[] { "/gateway/appointments", "/gateway/payments", "/gateway/doctors", "/gateway/branches", "/gateway/doctors/search", "/gateway/invoices", "/gateway/users" })
            host.Gateway.MapStatus("GET", path, 503);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Yönetim paneli", html);
        foreach (var key in new[] { "daily", "status", "revenue", "doctors" })
            Assert.Contains($"data-chart-missing=\"{key}\"", html);
        Assert.DoesNotContain("<canvas", html);
        Assert.DoesNotContain("stat-tile", html);
    }

    [Fact]
    public async Task Dashboard_draws_the_other_charts_when_only_the_payment_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSummaryEndpoints(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, new[] { Appointment() });
        host.Gateway.MapStatus("GET", "/gateway/payments", 503);
        host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { DoctorListItem() });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { Branch() });
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin"));

        Assert.Contains("id=\"chart-daily\"", html);
        Assert.Contains("id=\"chart-doctors\"", html);
        Assert.Contains("data-chart-missing=\"revenue\"", html);
        Assert.DoesNotContain("id=\"chart-revenue\"", html);
    }

    [Fact]
    public async Task Chart_data_sits_in_a_json_script_and_a_closing_script_tag_in_a_name_cannot_break_out()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapSummaryEndpoints(host);
        host.Gateway.MapJson("GET", "/gateway/appointments", 200, new[] { Appointment() });
        host.Gateway.MapJson("GET", "/gateway/payments", 200, Paged([Payment()], pageSize: 50, total: 1));
        host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { DoctorListItem(first: Evil) });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { Branch(name: Evil) });
        var client = await AdminClientAsync(host);

        var raw = await (await client.GetAsync("/Admin")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)", raw);
        Assert.DoesNotContain("</script><script>", raw);
        var json = EmbeddedJson(raw);
        Assert.DoesNotContain("<", json);
        Assert.DoesNotContain(">", json);
        using var doc = JsonDocument.Parse(json);
        var labels = doc.RootElement.GetProperty("doctorsByBranch").GetProperty("labels")[0].GetString();
        Assert.Equal(Evil, labels); // veri bozulmadan geri okunur, yalnız kodlanmıştır
        Assert.Contains("&lt;", raw); // tablolarda doktor adı HTML kodlu
    }
}

public class AdminReviewsTests
{
    private static object Review(string id = "rev1", string doctorId = DoctorId, int rating = 4, string comment = "Çok iyi", string by = "Hasta A", int day = 5)
        => new { id, doctorId, reviewerName = by, rating, comment, createdAt = new DateTime(2026, 10, day, 9, 0, 0, DateTimeKind.Utc), isMine = false };

    private static void MapDoctors(WebUiTestHost host) => host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { DoctorListItem() });

    [Theory]
    [InlineData("/Admin/Reviews")]
    [InlineData("/Admin/Patients")]
    public async Task Anonymous_is_redirected_and_patient_doctor_get_403(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var anonymous = await host.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));

        foreach (var role in new[] { "Patient", "Doctor" })
        {
            var client = await ClientAsync(host, role);
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/reviews") || r.Path == "/gateway/users");
    }

    [Fact]
    public async Task Index_lists_reviews_with_doctor_name_and_stars_and_encodes_the_comment()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/reviews", 200, new[] { Review(comment: "<script>alert(1)</script> & \"x\"", by: "<b>Zararlı</b>", rating: 3) });
        MapDoctors(host);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Reviews");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("<script>alert(1)", raw);
        Assert.DoesNotContain("<b>Zararlı", raw);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", raw);
        Assert.Contains("Dr. Ali Veli", raw);
        Assert.Contains("★★★<span class=\"off\">☆☆</span>", raw);
        Assert.Contains("/Admin/Reviews/Delete/rev1", raw);
        Assert.StartsWith("Bearer ", Last(host, "GET", "/gateway/reviews").Authorization);
    }

    [Fact]
    public async Task Doctor_filter_goes_to_the_api_and_rating_and_text_filters_apply_on_the_list()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var queries = new ConcurrentQueue<string>();
        host.Gateway.Map("GET", "/gateway/reviews", ctx =>
        {
            queries.Enqueue(ctx.Request.QueryString.Value ?? "");
            return ctx.Response.WriteAsJsonAsync(new[]
            {
                Review("r5", rating: 5, comment: "Harika doktor", by: "Ayşe"),
                Review("r2", rating: 2, comment: "Bekleme uzundu", by: "Mehmet"),
            });
        });
        MapDoctors(host);
        var client = await AdminClientAsync(host);

        var byRating = await HtmlAsync(await client.GetAsync($"/Admin/Reviews?doctorId={DoctorId}&rating=5"));
        var byText = await HtmlAsync(await client.GetAsync("/Admin/Reviews?search=bekleme"));

        Assert.Contains($"?doctorId={DoctorId}", queries);
        Assert.Contains("/Admin/Reviews/Delete/r5", byRating);
        Assert.DoesNotContain("/Admin/Reviews/Delete/r2", byRating);
        Assert.Contains("/Admin/Reviews/Delete/r2", byText);
        Assert.DoesNotContain("/Admin/Reviews/Delete/r5", byText);
    }

    [Fact]
    public async Task Delete_is_post_only_with_antiforgery_and_calls_the_right_delete_endpoint()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/reviews", 200, new[] { Review() });
        MapDoctors(host);
        host.Gateway.MapStatus("DELETE", "/gateway/reviews/rev1", 204);
        var client = await AdminClientAsync(host);

        var get = await client.GetAsync("/Admin/Reviews/Delete/rev1");                                   // GET ile silinmez
        var noToken = await client.PostAsync("/Admin/Reviews/Delete/rev1", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.True(get.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);                                      // antiforgery yok
        Assert.False(Called(host, "DELETE", "/gateway/reviews/rev1"));

        var ok = await PostAsync(host, client, "/Admin/Reviews", "/Admin/Reviews/Delete/rev1",
            new() { ["doctorId"] = DoctorId, ["rating"] = "4", ["page"] = "1" });

        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.StartsWith("/Admin/Reviews", Location(ok));
        Assert.Contains($"doctorId={DoctorId}", Location(ok));
        Assert.Single(host.Gateway.Requests, r => r.Method == "DELETE" && r.Path == "/gateway/reviews/rev1");
        Assert.StartsWith("Bearer ", Last(host, "DELETE", "/gateway/reviews/rev1").Authorization);
    }

    [Fact]
    public async Task Delete_of_an_already_removed_review_shows_a_warning_instead_of_an_error_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/reviews", 200, new[] { Review() });
        MapDoctors(host);
        host.Gateway.MapStatus("DELETE", "/gateway/reviews/rev1", 404);
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin/Reviews", "/Admin/Reviews/Delete/rev1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var follow = await HtmlAsync(await client.GetAsync(Location(response)));
        Assert.Contains("bulunamadı", follow);
    }

    [Fact]
    public async Task Patient_cannot_post_a_review_delete()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await ClientAsync(host, "Patient");

        var response = await PostAsync(host, client, "/", "/Admin/Reviews/Delete/rev1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "DELETE");
    }

    [Fact]
    public async Task Menu_has_reviews_and_patients_links()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin/Users"));

        Assert.Contains("href=\"/Admin/Reviews\"", html);
        Assert.Contains("href=\"/Admin/Patients\"", html);
    }
}

public class AdminPatientsTests
{
    [Fact]
    public async Task Index_asks_for_role_patient_pages_it_and_never_fetches_appointments()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var queries = new ConcurrentQueue<string>();
        host.Gateway.Map("GET", "/gateway/users", ctx =>
        {
            queries.Enqueue(ctx.Request.QueryString.Value ?? "");
            return ctx.Response.WriteAsJsonAsync(Paged([User()], page: 2, pageSize: 20, total: 45));
        });
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin/Patients?search=zey&page=2");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.Single(queries);
        Assert.Contains("role=Patient", query);
        Assert.Contains("page=2", query);
        Assert.Contains("pageSize=20", query);
        Assert.Contains("search=zey", query);
        Assert.Contains("Zeynep Kaya", html);
        Assert.Contains("href=\"/Admin/Users/Details/user-9\"", html);
        Assert.Contains("href=\"/Admin/Appointments?PatientId=user-9\"", html);
        Assert.Contains("Toplam 45 hasta", html);
        Assert.False(Called(host, "GET", "/gateway/appointments")); // N+1 yok: satırda randevu sayısı yok
        Assert.StartsWith("Bearer ", Last(host, "GET", "/gateway/users").Authorization);
    }

    [Fact]
    public async Task Empty_list_says_no_patient_and_bad_filter_shows_a_form_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/users", 200, Paged([]));
        var client = await AdminClientAsync(host);
        Assert.Contains("Hasta bulunamadı", await HtmlAsync(await client.GetAsync("/Admin/Patients")));

        host.MapProblem("GET", "/gateway/users", 400, "Geçersiz süzgeç.");
        var html = await HtmlAsync(await client.GetAsync("/Admin/Patients"));
        Assert.Contains("Hasta bulunamadı", html);
    }

    [Fact]
    public async Task Patient_names_are_html_encoded()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/users", 200, Paged([new { id = "u1", firstName = "<img src=x onerror=alert(1)>", lastName = "Z", email = "a@b.c", roles = new[] { "Patient" }, doctorId = (string?)null }]));
        var client = await AdminClientAsync(host);

        var raw = await (await client.GetAsync("/Admin/Patients")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("<img src=x", raw);
        Assert.Contains("&lt;img src=x", raw);
    }
}
