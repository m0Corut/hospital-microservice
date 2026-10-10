using System.Net;
using static Hastane.WebUI.Tests.AdminPanelTestHelpers;

namespace Hastane.WebUI.Tests;

// Yönetim paneli: özet kutuları, randevu durum değiştirme, reçete düzenle/sil/yazdır. Sahte gateway deseni (UI-KURALLARI §7).
public class AdminHomeSummaryTests
{
    [Fact]
    public async Task Home_shows_only_counts_that_the_paged_endpoints_report()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/doctors/search", 200, Paged([DoctorListItem()], page: 1, pageSize: 1, total: 42));
        host.Gateway.MapJson("GET", "/gateway/payments", 200, Paged([], page: 1, pageSize: 1, total: 17));
        host.Gateway.MapStatus("GET", "/gateway/invoices", 503); // fatura servisi kapalı: kutu hiç gösterilmez
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync("/Admin");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(">42<", html);
        Assert.Contains(">17<", html);
        Assert.Contains("Doktor", html);
        Assert.Contains("Ödeme", html);
        Assert.DoesNotContain("st-label\">Fatura<", html); // uydurma sıfır yok
        Assert.Contains("yakında", html); // randevu/reçete sayısı hesaplanamıyor, söylenir
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method != "GET" && r.Path != "/gateway/auth/login");
        Assert.StartsWith("Bearer ", Last(host, "GET", "/gateway/doctors/search").Authorization);
    }

    [Fact]
    public async Task Home_asks_each_endpoint_for_a_single_row()
    {
        await using var host = await WebUiTestHost.StartAsync();
        // Grafikler de ödemeleri çektiği için ödeme ucu birden çok sorgu alır; özet kutusunun pageSize=1 sorgusu bunların arasında olmalı.
        var doctors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var payments = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var invoices = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Record(string path, System.Collections.Concurrent.ConcurrentQueue<string> sink, int total)
            => host.Gateway.Map("GET", path, ctx =>
            {
                sink.Enqueue(ctx.Request.QueryString.Value ?? "");
                ctx.Response.StatusCode = 200;
                return ctx.Response.WriteAsJsonAsync(Paged([], page: 1, pageSize: 1, total: total));
            });
        Record("/gateway/doctors/search", doctors, 3);
        Record("/gateway/payments", payments, 4);
        Record("/gateway/invoices", invoices, 5);
        var client = await AdminClientAsync(host);

        await client.GetAsync("/Admin");

        Assert.Contains("?page=1&pageSize=1", doctors);
        Assert.Contains("?page=1&pageSize=1", payments);
        Assert.Equal(["?page=1&pageSize=1"], invoices);
    }

    [Fact]
    public async Task Home_without_any_service_still_opens_without_tiles()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host); // hiçbir yol tanımlı değil: sahte gateway hepsine 404 verir

        var response = await client.GetAsync("/Admin");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("stat-tile", html);
        Assert.Contains("Yönetim paneli", html);
    }

    [Fact]
    public async Task Panel_shell_shows_profile_card_and_all_admin_menu_links()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync("/Admin"));

        Assert.Contains("panel-profile", html);
        Assert.Contains("Yönetici", html);
        foreach (var path in new[] { "/Admin/Branches", "/Admin/Doctors", "/Admin/Users", "/Admin/Roles", "/Admin/Appointments", "/Admin/Payments", "/Admin/Invoices", "/Admin/Prescriptions" })
            Assert.Contains($"href=\"{path}\"", html);
    }
}

public class AdminAppointmentStatusTests
{
    private static void MapLookups(WebUiTestHost host)
    {
        host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { DoctorListItem() });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { Branch() });
    }

    private static object WithStatus(string status) => new
    {
        id = AppointmentId,
        doctorId = DoctorId,
        patientId = "patient-guid-1",
        branchId = BranchId,
        appointmentDate = new DateTime(2026, 10, 20, 9, 30, 0, DateTimeKind.Utc),
        status,
        createdAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
    };

    [Theory]
    [InlineData("Pending", new[] { "Onayla", "İptal et" }, new[] { "Tamamla" })]
    [InlineData("Confirmed", new[] { "Tamamla", "İptal et" }, new[] { "Onayla" })]
    [InlineData("Completed", new string[0], new[] { "Onayla", "Tamamla", "İptal et" })]
    [InlineData("Cancelled", new string[0], new[] { "Onayla", "Tamamla", "İptal et" })]
    public async Task Details_offers_only_transitions_the_state_machine_allows(string status, string[] visible, string[] hidden)
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        host.Gateway.MapJson("GET", $"/gateway/appointments/{AppointmentId}", 200, WithStatus(status));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync($"/Admin/Appointments/Details/{AppointmentId}"));

        foreach (var label in visible) Assert.Contains($">{label}<", html);
        foreach (var label in hidden) Assert.DoesNotContain($">{label}<", html);
    }

    [Fact]
    public async Task SetStatus_patches_the_gateway_and_redirects_to_details()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("PATCH", $"/gateway/appointments/{AppointmentId}/status", 200, WithStatus("Confirmed"));
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin", $"/Admin/Appointments/SetStatus/{AppointmentId}", new() { ["status"] = "Confirmed" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Appointments/Details/{AppointmentId}", Location(response));
        var sent = Last(host, "PATCH", $"/gateway/appointments/{AppointmentId}/status");
        Assert.Equal("Confirmed", Json(sent).GetProperty("status").GetString());
        Assert.StartsWith("Bearer ", sent.Authorization);
    }

    [Fact]
    public async Task SetStatus_conflict_is_shown_as_warning_on_the_next_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapLookups(host);
        host.Gateway.MapJson("PATCH", $"/gateway/appointments/{AppointmentId}/status", 409,
            new { status = 409, title = "Çakışma", detail = "Completed durumundan Cancelled durumuna geçilemez." });
        host.Gateway.MapJson("GET", $"/gateway/appointments/{AppointmentId}", 200, WithStatus("Completed"));
        var client = await AdminClientAsync(host);

        var post = await PostAsync(host, client, "/Admin", $"/Admin/Appointments/SetStatus/{AppointmentId}", new() { ["status"] = "Cancelled" });
        var html = await HtmlAsync(await client.GetAsync(Location(post)!));

        Assert.Contains("geçilemez", html);
    }

    [Fact]
    public async Task SetStatus_with_unknown_status_never_reaches_the_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin", $"/Admin/Appointments/SetStatus/{AppointmentId}", new() { ["status"] = "Bozuk" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(Called(host, "PATCH", $"/gateway/appointments/{AppointmentId}/status"));
    }

    [Fact]
    public async Task SetStatus_is_admin_only_and_requires_antiforgery()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var doctor = await ClientAsync(host, "Doctor");
        var admin = await AdminClientAsync(host);

        var forbidden = await PostAsync(host, doctor, "/Error/404", $"/Admin/Appointments/SetStatus/{AppointmentId}", new() { ["status"] = "Confirmed" });
        var noToken = await admin.PostAsync($"/Admin/Appointments/SetStatus/{AppointmentId}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["status"] = "Confirmed" }));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.False(Called(host, "PATCH", $"/gateway/appointments/{AppointmentId}/status"));
    }
}

public class AdminPrescriptionEditTests
{
    private static void MapPrescription(WebUiTestHost host)
    {
        host.Gateway.MapJson("GET", "/gateway/doctors", 200, new[] { DoctorListItem() });
        host.Gateway.MapJson("GET", "/gateway/branches", 200, new[] { Branch() });
        host.Gateway.MapJson("GET", $"/gateway/prescriptions/{PrescriptionId}", 200, new
        {
            id = PrescriptionId,
            appointmentId = AppointmentId,
            doctorId = DoctorId,
            patientId = "p1",
            issuedAt = new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc),
            notes = "Bol sıvı",
            items = new[] { new { id = Guid.NewGuid(), lineNo = 1, medicineName = "Parol", dosage = "500 mg", frequency = "Günde 3", durationDays = 5, instructions = "Tok karnına" } },
        });
    }

    private static Dictionary<string, string> ValidForm() => new()
    {
        ["Notes"] = "Yeni not",
        ["Items[0].MedicineName"] = "Aferin",
        ["Items[0].Dosage"] = "1 tablet",
        ["Items[0].Frequency"] = "Günde 2",
        ["Items[0].DurationDays"] = "7",
        ["Items[0].Instructions"] = "Tok",
    };

    [Fact]
    public async Task Edit_form_is_filled_from_the_api_and_posts_to_the_admin_area()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPrescription(host);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync($"/Admin/Prescriptions/Edit/{PrescriptionId}");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Reçeteyi düzenle", html);
        Assert.Contains("value=\"Parol\"", html);
        Assert.Contains("Bol sıvı", html);
        Assert.Contains($"action=\"/Admin/Prescriptions/Edit/{PrescriptionId}\"", html);
        Assert.Contains("Hasta Id: p1", html);
    }

    [Fact]
    public async Task Edit_post_puts_notes_and_items_then_redirects_to_details()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPrescription(host);
        host.Gateway.MapJson("PUT", $"/gateway/prescriptions/{PrescriptionId}", 200, new { id = PrescriptionId });
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin", $"/Admin/Prescriptions/Edit/{PrescriptionId}", ValidForm());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Prescriptions/Details/{PrescriptionId}", Location(response));
        var sent = Last(host, "PUT", $"/gateway/prescriptions/{PrescriptionId}");
        var body = Json(sent);
        Assert.StartsWith("Bearer ", sent.Authorization);
        Assert.Equal(PrescriptionId, body.GetProperty("id").GetGuid());
        Assert.Equal("Yeni not", body.GetProperty("notes").GetString());
        Assert.Equal("Aferin", body.GetProperty("items")[0].GetProperty("medicineName").GetString());
        Assert.Equal(7, body.GetProperty("items")[0].GetProperty("durationDays").GetInt32());
        Assert.False(body.TryGetProperty("doctorId", out _)); // reçete sahibi değişmez
    }

    [Fact]
    public async Task Edit_invalid_form_and_api_400_stay_on_the_form_without_losing_input()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPrescription(host);
        var client = await AdminClientAsync(host);

        var invalid = ValidForm();
        invalid["Items[0].MedicineName"] = "";
        var localHtml = await HtmlAsync(await PostAsync(host, client, "/Admin", $"/Admin/Prescriptions/Edit/{PrescriptionId}", invalid));
        Assert.Contains("İlaç adı gerekli.", localHtml);
        Assert.False(Called(host, "PUT", $"/gateway/prescriptions/{PrescriptionId}"));

        host.Gateway.MapJson("PUT", $"/gateway/prescriptions/{PrescriptionId}", 400, new { status = 400, title = "Geçersiz", detail = "Reçete kaydedilemedi." });
        var apiHtml = await HtmlAsync(await PostAsync(host, client, "/Admin", $"/Admin/Prescriptions/Edit/{PrescriptionId}", ValidForm()));
        Assert.Contains("Reçete kaydedilemedi.", apiHtml);
        Assert.Contains("value=\"Aferin\"", apiHtml);
    }

    [Fact]
    public async Task Edit_add_item_button_redraws_the_form_without_saving()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPrescription(host);
        var client = await AdminClientAsync(host);
        var fields = ValidForm();
        fields["op"] = "add-item";

        var html = await HtmlAsync(await PostAsync(host, client, "/Admin", $"/Admin/Prescriptions/Edit/{PrescriptionId}", fields));

        Assert.Contains("name=\"Items[1].MedicineName\"", html);
        Assert.False(Called(host, "PUT", $"/gateway/prescriptions/{PrescriptionId}"));
    }

    [Fact]
    public async Task Delete_post_deletes_through_the_gateway_and_returns_to_the_list()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPrescription(host);
        host.Gateway.MapStatus("DELETE", $"/gateway/prescriptions/{PrescriptionId}", 204);
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/Admin", $"/Admin/Prescriptions/Delete/{PrescriptionId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Prescriptions", Location(response));
        Assert.StartsWith("Bearer ", Last(host, "DELETE", $"/gateway/prescriptions/{PrescriptionId}").Authorization);
    }

    [Fact]
    public async Task Delete_get_is_not_allowed_and_wrong_roles_are_blocked()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPrescription(host);
        var admin = await AdminClientAsync(host);
        var doctor = await ClientAsync(host, "Doctor");

        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync($"/Admin/Prescriptions/Delete/{PrescriptionId}")).StatusCode); // yalnız POST: GET ile silinmez
        foreach (var path in new[] { $"/Admin/Prescriptions/Edit/{PrescriptionId}", $"/Admin/Prescriptions/Print/{PrescriptionId}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync(path)).StatusCode);
        Assert.False(Called(host, "DELETE", $"/gateway/prescriptions/{PrescriptionId}"));
    }

    [Fact]
    public async Task Print_page_has_no_menu_and_shows_items()
    {
        await using var host = await WebUiTestHost.StartAsync();
        MapPrescription(host);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync($"/Admin/Prescriptions/Print/{PrescriptionId}");
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("window.print()", html);
        Assert.Contains("Parol", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("Bol sıvı", html);
        Assert.DoesNotContain("site-nav", html);
        Assert.DoesNotContain("panel-side", html);
        Assert.DoesNotContain("Çıkış yap", html);
    }
}
