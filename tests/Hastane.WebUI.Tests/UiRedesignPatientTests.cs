using System.Net;
using static Hastane.WebUI.Tests.PublicPatientTestHelpers;

namespace Hastane.WebUI.Tests;

// Hasta paneli: randevu onay sayfası, ödeme özeti, yeniden planlama, yazdırma. Sahte gateway deseni (UI-KURALLARI §7).
public class PatientConfirmationAndCheckoutTests
{
    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync(string role = "Patient", bool paid = false)
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, Appointment(Appt1));
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        host.Gateway.MapJson("GET", "/gateway/doctors/d1", 200, Doctor());
        host.Gateway.MapJson("GET", "/gateway/payments", 200, paid
            ? new { items = new[] { Payment(Pay1, "Paid") }, totalCount = 1, page = 1, pageSize = 10 }
            : EmptyPayments());
        return (host, await host.LoginAsAsync(role));
    }

    [Fact]
    public async Task Confirmation_shows_summary_pay_now_and_my_appointments()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await client.GetAsync($"/Patient/Appointments/Confirmation/{Appt1}");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Randevunuz alındı", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("Onay bekliyor", html);
        Assert.Contains($"/Patient/Payments/Create?appointmentId={Appt1}", html);
        Assert.Contains("Şimdi öde", html);
        Assert.Contains("href=\"/Patient/Appointments\"", html);
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", $"/gateway/appointments/{Appt1}").Last().Authorization);
    }

    [Fact]
    public async Task Confirmation_hides_pay_now_when_already_paid()
    {
        var (host, client) = await StartAsync(paid: true);
        await using var _h = host;

        var html = await (await client.GetAsync($"/Patient/Appointments/Confirmation/{Appt1}")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("Şimdi öde", html);
        Assert.Contains("Ödendi", html);
    }

    [Fact]
    public async Task Confirmation_still_opens_when_payment_service_is_down()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("GET", "/gateway/payments", 503);

        var response = await client.GetAsync($"/Patient/Appointments/Confirmation/{Appt1}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Şimdi öde", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Confirmation_is_404_for_unknown_appointment_and_blocked_for_other_roles()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("GET", $"/gateway/appointments/{Appt2}", 404);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Patient/Appointments/Confirmation/{Appt2}")).StatusCode);

        var doctor = await host.LoginAsAsync("Doctor");
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync($"/Patient/Appointments/Confirmation/{Appt1}")).StatusCode);
        var anonymous = await host.CreateClient().GetAsync($"/Patient/Appointments/Confirmation/{Appt1}");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));
    }

    [Fact]
    public async Task Checkout_shows_stepper_and_payment_summary_above_the_card_form()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var html = await (await client.GetAsync($"/Patient/Payments/Create?appointmentId={Appt1}")).Content.ReadHtmlAsync();

        Assert.Contains("class=\"stepper\"", html);
        Assert.Contains("Ödeme özeti", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("500,00 TRY", html);
        Assert.Contains("name=\"CardNumber\"", html);
        Assert.DoesNotContain("name=\"CardNumber\" value=", html);
    }
}

public class PatientRescheduleTests
{
    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync(string status = "Confirmed")
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, Appointment(Appt1, status));
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        host.Gateway.MapJson("GET", "/gateway/payments", 200, EmptyPayments());
        return (host, await host.LoginAsAsync("Patient"));
    }

    [Fact]
    public async Task Details_offers_reschedule_only_for_pending_or_confirmed()
    {
        var (host, client) = await StartAsync("Confirmed");
        await using var _h = host;
        var open = await (await client.GetAsync($"/Patient/Appointments/Details/{Appt1}")).Content.ReadHtmlAsync();
        Assert.Contains($"/Patient/Appointments/Reschedule/{Appt1}", open);

        host.Gateway.MapJson("GET", $"/gateway/appointments/{Appt1}", 200, Appointment(Appt1, "Completed"));
        var closed = await (await client.GetAsync($"/Patient/Appointments/Details/{Appt1}")).Content.ReadHtmlAsync();
        Assert.DoesNotContain("/Reschedule/", closed);
    }

    [Fact]
    public async Task Form_shows_doctor_and_current_date()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var html = await (await client.GetAsync($"/Patient/Appointments/Reschedule/{Appt1}")).Content.ReadHtmlAsync();

        Assert.Contains("Randevuyu yeniden planla", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.Contains("datetime-local", html);
    }

    [Fact]
    public async Task Form_for_finished_appointment_redirects_back_without_form()
    {
        var (host, client) = await StartAsync("Cancelled");
        await using var _h = host;

        var response = await client.GetAsync($"/Patient/Appointments/Reschedule/{Appt1}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Appointments/Details/{Appt1}", Location(response));
    }

    [Fact]
    public async Task Post_puts_utc_date_with_branch_from_api_and_ignores_form_branch()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("PUT", $"/gateway/appointments/{Appt1}", 200, Appointment(Appt1));
        var localText = LocalFuture(7);
        var expectedUtc = DateTime.SpecifyKind(DateTime.Parse(localText, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Local).ToUniversalTime();

        var response = await host.PostAsync(client, $"/Patient/Appointments/Reschedule/{Appt1}", new()
        {
            ["AppointmentLocal"] = localText,
            ["BranchId"] = "kotu-sube",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Appointments/Details/{Appt1}", Location(response));
        var sent = host.Gateway.Calls("PUT", $"/gateway/appointments/{Appt1}").Single();
        Assert.StartsWith("Bearer ", sent.Authorization);
        var json = Json(sent.Body);
        Assert.Equal(Appt1, json.GetProperty("id").GetGuid());
        Assert.Equal("b0", json.GetProperty("branchId").GetString());
        Assert.Equal(expectedUtc, json.GetProperty("appointmentDate").GetDateTime());
        Assert.False(json.TryGetProperty("doctorId", out _));
        Assert.False(json.TryGetProperty("patientId", out _));
    }

    [Fact]
    public async Task Post_409_slot_taken_is_shown_in_form_and_keeps_the_entered_date()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("PUT", $"/gateway/appointments/{Appt1}", 409, Problem(409, "Bu doktorun bu saatte başka bir randevusu var."));
        var localText = LocalFuture(7);

        var response = await host.PostAsync(client, $"/Patient/Appointments/Reschedule/{Appt1}", new() { ["AppointmentLocal"] = localText });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bu doktorun bu saatte başka bir randevusu var.", html);
        Assert.Contains(localText, html);
    }

    [Fact]
    public async Task Post_with_past_or_missing_date_never_reaches_the_api()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var past = DateTime.Now.AddDays(-2).ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);

        var pastHtml = await (await host.PostAsync(client, $"/Patient/Appointments/Reschedule/{Appt1}", new() { ["AppointmentLocal"] = past })).Content.ReadHtmlAsync();
        var missingHtml = await (await host.PostAsync(client, $"/Patient/Appointments/Reschedule/{Appt1}")).Content.ReadHtmlAsync();

        Assert.Contains("Randevu tarihi gelecekte olmalı.", pastHtml);
        Assert.Contains("Tarih ve saat seçin.", missingHtml);
        Assert.Empty(host.Gateway.Calls("PUT", $"/gateway/appointments/{Appt1}"));
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await client.PostAsync($"/Patient/Appointments/Reschedule/{Appt1}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["AppointmentLocal"] = LocalFuture(7) }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("PUT", $"/gateway/appointments/{Appt1}"));
    }

    [Fact]
    public async Task Other_roles_and_anonymous_cannot_reschedule_as_patient()
    {
        var (host, _) = await StartAsync();
        await using var _h = host;
        var doctor = await host.LoginAsAsync("Doctor");

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync($"/Patient/Appointments/Reschedule/{Appt1}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await host.CreateClient().GetAsync($"/Patient/Appointments/Reschedule/{Appt1}")).StatusCode);
        Assert.Empty(host.Gateway.Calls("PUT", $"/gateway/appointments/{Appt1}"));
    }
}

public class PatientPrintTests
{
    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync(string role = "Patient")
    {
        var host = await WebUiTestHost.StartAsync();
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
        host.Gateway.MapJson("GET", $"/gateway/prescriptions/{Rx1}", 200, new
        {
            id = Rx1,
            appointmentId = Appt1,
            doctorId = DoctorId,
            issuedAt = DateTime.UtcNow,
            notes = "Bol su için",
            items = new[] { new { id = Guid.NewGuid(), lineNo = 1, medicineName = "Parasetamol", dosage = "500 mg", frequency = "Günde 3", durationDays = 5, instructions = "Tok karnına" } },
        });
        host.Gateway.MapJson("GET", "/gateway/doctors/batch", 200, new[] { DoctorSummary() });
        return (host, await host.LoginAsAsync(role));
    }

    private static void AssertPrintableShell(string html)
    {
        Assert.Contains("window.print()", html);
        Assert.Contains("Yazdır", html);
        Assert.DoesNotContain("site-nav", html); // üst menü yok
        Assert.DoesNotContain("site-footer", html); // alt bilgi yok
        Assert.DoesNotContain("Çıkış yap", html);
        Assert.DoesNotContain("data-bs-toggle=\"dropdown\"", html);
    }

    [Fact]
    public async Task Invoice_print_page_is_menu_less_and_calls_the_invoice_endpoint()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await client.GetAsync($"/Patient/Invoices/Print/{Inv1}");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrintableShell(html);
        Assert.Contains("INV-0001", html);
        Assert.Contains("KDV (%10)", html);
        Assert.Contains("Dr. Ali Veli", html);
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", $"/gateway/invoices/{Inv1}").Last().Authorization);
    }

    [Fact]
    public async Task Prescription_print_page_is_menu_less_and_lists_items()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var html = await (await client.GetAsync($"/Patient/Prescriptions/Print/{Rx1}")).Content.ReadHtmlAsync();

        AssertPrintableShell(html);
        Assert.Contains("Parasetamol", html);
        Assert.Contains("5 gün", html);
        Assert.Contains("Bol su için", html);
    }

    [Fact]
    public async Task Details_pages_link_to_print_views()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var invoice = await (await client.GetAsync($"/Patient/Invoices/Details/{Inv1}")).Content.ReadHtmlAsync();
        var prescription = await (await client.GetAsync($"/Patient/Prescriptions/Details/{Rx1}")).Content.ReadHtmlAsync();

        Assert.Contains($"/Patient/Invoices/Print/{Inv1}", invoice);
        Assert.Contains($"/Patient/Prescriptions/Print/{Rx1}", prescription);
    }

    [Theory]
    [InlineData("/Patient/Invoices/Print/55555555-5555-5555-5555-555555555555")]
    [InlineData("/Patient/Prescriptions/Print/33333333-3333-3333-3333-333333333333")]
    public async Task Print_pages_reject_other_roles_and_anonymous_without_calling_the_api(string path)
    {
        var (host, _) = await StartAsync();
        await using var _h = host;
        var doctor = await host.LoginAsAsync("Doctor");
        var afterLogin = host.Gateway.Requests.Count;

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync(path)).StatusCode);
        var anonymous = await host.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));
        Assert.Equal(afterLogin, host.Gateway.Requests.Count);
    }

    [Fact]
    public async Task Unknown_invoice_print_is_404()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var id = Guid.NewGuid();
        host.Gateway.MapStatus("GET", $"/gateway/invoices/{id}", 404);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Patient/Invoices/Print/{id}")).StatusCode);
    }
}
