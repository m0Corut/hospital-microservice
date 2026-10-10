using System.Net;
using static Hastane.WebUI.Tests.PublicPatientTestHelpers;

namespace Hastane.WebUI.Tests;

// Sipariş ekranları: /Checkout, /Patient/Orders, /Admin/Orders. Sahte gateway deseni (UI-KURALLARI §7).
internal static class OrderUiData
{
    public static readonly Guid Order1 = Guid.Parse("66666666-6666-6666-6666-666666666666");
    public static readonly Guid Order2 = Guid.Parse("77777777-7777-7777-7777-777777777777");
    public static readonly Guid Product1 = Guid.Parse("88888888-8888-8888-8888-888888888888");

    public static object Cart(params (string Name, int Qty, decimal Price)[] lines) => new
    {
        items = lines.Select(l => new { productId = Product1, name = l.Name, unitPrice = l.Price, quantity = l.Qty, lineTotal = l.Price * l.Qty }).ToArray(),
        total = lines.Sum(l => l.Price * l.Qty),
        currency = "TRY",
    };

    public static object OrderJson(Guid id, string number = "SIP-2026-000123", string status = "Received",
        string? cancelReason = null, bool releasePending = false) => new
        {
            id,
            orderNumber = number,
            patientUserId = "user-1",
            patientName = "Ayşe Yılmaz",
            shippingAddress = "Atatürk Cad. No:1 Kadıköy/İstanbul",
            note = "Zili çalmayın",
            status,
            totalAmount = 501.00m,
            currency = "TRY",
            paymentMethod = "CashOnDelivery",
            createdAt = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc),
            updatedAt = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc),
            cancelReason,
            stockReleasePending = releasePending,
            items = new[] { new { productId = Product1, productName = "Tansiyon aleti", unitPrice = 250.50m, quantity = 2, lineTotal = 501.00m } },
        };

    public static object OrderPage(params (Guid Id, string Number, string Status)[] rows) => new
    {
        items = rows.Select(r => new
        {
            id = r.Id,
            orderNumber = r.Number,
            patientUserId = "user-1",
            patientName = "Ayşe Yılmaz",
            status = r.Status,
            totalAmount = 501.00m,
            currency = "TRY",
            itemCount = 1,
            createdAt = new DateTime(2026, 10, 10, 9, 30, 0, DateTimeKind.Utc),
        }).ToArray(),
        totalCount = rows.Length,
        page = 1,
        pageSize = 10,
    };
}

public class CheckoutUiTests
{
    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync(string role = "Patient", object? cart = null, bool profileAddress = true)
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, cart ?? OrderUiData.Cart(("Tansiyon aleti", 2, 250.50m)));
        if (profileAddress)
            host.Gateway.MapJson("GET", "/gateway/patients/me", 200, new { userId = "user-1", address = "Moda Cad. 5", city = "İstanbul" });
        else
            host.Gateway.MapStatus("GET", "/gateway/patients/me", 404);
        return (host, await host.LoginAsAsync(role));
    }

    private static Dictionary<string, string> Form(string key, string address = "Atatürk Cad. No:1", string? note = null)
        => new() { ["ShippingAddress"] = address, ["Note"] = note ?? string.Empty, ["IdempotencyKey"] = key };

    [Fact]
    public async Task Get_shows_cart_summary_cash_on_delivery_and_prefills_address_from_profile()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await client.GetAsync("/Checkout");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sepet özeti", html);
        Assert.Contains("Tansiyon aleti", html);
        Assert.Contains("501,00 TRY", html);
        Assert.Contains("Kapıda ödeme", html);
        Assert.Contains("Moda Cad. 5, İstanbul", html);
        Assert.Contains("name=\"IdempotencyKey\"", html);
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/cart").Last().Authorization);
    }

    [Fact]
    public async Task Get_leaves_address_empty_when_profile_has_none()
    {
        var (host, client) = await StartAsync(profileAddress: false);
        await using var _h = host;

        var html = await (await client.GetAsync("/Checkout")).Content.ReadHtmlAsync();

        Assert.Contains("Teslimat bilgileri", html);
        Assert.DoesNotContain("Adres profilinizden dolduruldu", html);
    }

    [Fact]
    public async Task Get_with_empty_cart_shows_empty_state_and_no_form()
    {
        var (host, client) = await StartAsync(cart: OrderUiData.Cart());
        await using var _h = host;

        var html = await (await client.GetAsync("/Checkout")).Content.ReadHtmlAsync();

        Assert.Contains("Sepetiniz boş", html);
        Assert.DoesNotContain("Siparişi ver", html);
    }

    [Fact]
    public async Task Get_when_market_is_down_shows_warning_instead_of_error_page()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("GET", "/gateway/cart", 503);

        var response = await client.GetAsync("/Checkout");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ulaşılamıyor", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Post_success_sends_idempotency_key_without_prices_and_redirects_to_order_details()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var log = host.Gateway.MapLogged("POST", "/gateway/orders/checkout", 201, OrderUiData.OrderJson(OrderUiData.Order1));
        host.Gateway.MapJson("GET", $"/gateway/orders/{OrderUiData.Order1}", 200, OrderUiData.OrderJson(OrderUiData.Order1));
        var key = ExtractIdempotencyKey(await (await client.GetAsync("/Checkout")).Content.ReadAsStringAsync());

        var fields = Form(key, "Atatürk Cad. No:1", "Zili çalmayın");
        fields["TotalAmount"] = "0.01"; // kurcalanmış alan: API'ye gitmemeli
        fields["Cart.Total"] = "0.01";
        var response = await host.PostFormAsync(client, "/Checkout", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Orders/Details/{OrderUiData.Order1}", Location(response));
        Assert.Equal(key, Assert.Single(log.Keys));
        var sent = host.Gateway.Calls("POST", "/gateway/orders/checkout").Single();
        Assert.StartsWith("Bearer ", sent.Authorization);
        var body = Json(sent.Body);
        Assert.Equal("Atatürk Cad. No:1", body.GetProperty("shippingAddress").GetString());
        Assert.Equal("Zili çalmayın", body.GetProperty("note").GetString());
        Assert.Equal(2, body.EnumerateObject().Count()); // fiyat/tutar/hasta kimliği/durum yok

        var next = await (await client.GetAsync(Location(response))).Content.ReadHtmlAsync();
        Assert.Contains("Siparişiniz alındı", next);
        Assert.Contains("SIP-2026-000123", next);
    }

    [Fact]
    public async Task Post_without_address_shows_validation_error_and_does_not_call_the_api()
    {
        var (host, client) = await StartAsync(profileAddress: false);
        await using var _h = host;
        host.Gateway.MapStatus("POST", "/gateway/orders/checkout", 201);

        var response = await host.PostFormAsync(client, "/Checkout", Form("0123456789abcdef0123456789abcdef", address: ""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Teslimat adresi zorunlu", await response.Content.ReadHtmlAsync());
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/orders/checkout"));
    }

    [Fact]
    public async Task Post_with_too_long_address_or_note_is_rejected_before_the_api()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("POST", "/gateway/orders/checkout", 201);

        var response = await host.PostFormAsync(client, "/Checkout",
            Form("0123456789abcdef0123456789abcdef", new string('a', 301), new string('n', 501)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(host.Gateway.Calls("POST", "/gateway/orders/checkout"));
    }

    [Fact]
    public async Task Post_stock_conflict_shows_message_keeps_cart_and_issues_a_new_key()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.MapProblem("POST", "/gateway/orders/checkout", 409, "Stok yetersiz. Sepetinizdeki miktarları azaltıp tekrar deneyin.");
        var firstKey = ExtractIdempotencyKey(await (await client.GetAsync("/Checkout")).Content.ReadAsStringAsync());

        var response = await host.PostFormAsync(client, "/Checkout", Form(firstKey));
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Stok yetersiz", html);
        Assert.Contains("Tansiyon aleti", html); // sepet özeti yeniden çizildi
        Assert.NotEqual(firstKey, ExtractIdempotencyKey(html)); // iptal edilmiş niyet kapandı: yeni anahtar
    }

    [Fact]
    public async Task Post_when_order_service_is_down_shows_retry_message_not_the_503_page()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("POST", "/gateway/orders/checkout", 503);

        var response = await host.PostFormAsync(client, "/Checkout", Form("0123456789abcdef0123456789abcdef"));
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("işlenemedi", html);
        Assert.Contains("Siparişi ver", html);
    }

    [Fact]
    public async Task Post_empty_cart_from_api_shows_problem_message()
    {
        var (host, client) = await StartAsync(cart: OrderUiData.Cart());
        await using var _h = host;
        host.MapProblem("POST", "/gateway/orders/checkout", 400, "Sepetiniz boş.");

        var response = await host.PostFormAsync(client, "/Checkout", Form("0123456789abcdef0123456789abcdef"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sepetiniz boş", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Checkout_is_for_patients_only_and_anonymous_goes_to_login()
    {
        var (host, patient) = await StartAsync();
        await using var _h = host;
        _ = patient;

        var doctor = await host.LoginAsAsync("Doctor");
        var admin = await host.LoginAsAsync("Admin");
        var anonymous = await host.CreateClient().GetAsync("/Checkout");

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/Checkout")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/Checkout")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));
    }
}

public class PatientOrdersUiTests
{
    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync(string role = "Patient")
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/orders", 200, OrderUiData.OrderPage(
            (OrderUiData.Order1, "SIP-2026-000123", "Received"), (OrderUiData.Order2, "SIP-2026-000122", "Delivered")));
        host.Gateway.MapJson("GET", $"/gateway/orders/{OrderUiData.Order1}", 200, OrderUiData.OrderJson(OrderUiData.Order1));
        host.Gateway.MapJson("GET", $"/gateway/orders/{OrderUiData.Order2}", 200, OrderUiData.OrderJson(OrderUiData.Order2, "SIP-2026-000122", "Preparing"));
        return (host, await host.LoginAsAsync(role));
    }

    [Fact]
    public async Task List_shows_orders_with_status_badges_and_detail_links()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await client.GetAsync("/Patient/Orders");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("SIP-2026-000123", html);
        Assert.Contains("Alındı", html);
        Assert.Contains("Teslim edildi", html);
        Assert.Contains($"/Patient/Orders/Details/{OrderUiData.Order1}", html);
        Assert.StartsWith("Bearer ", host.Gateway.Calls("GET", "/gateway/orders").Last().Authorization);
    }

    [Fact]
    public async Task List_shows_empty_message_when_there_are_no_orders()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("GET", "/gateway/orders", 200, OrderUiData.OrderPage());

        Assert.Contains("Henüz siparişiniz yok", await (await client.GetAsync("/Patient/Orders")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Details_shows_snapshot_items_address_cash_on_delivery_and_cancel_for_received()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var html = await (await client.GetAsync($"/Patient/Orders/Details/{OrderUiData.Order1}")).Content.ReadHtmlAsync();

        Assert.Contains("SIP-2026-000123", html);
        Assert.Contains("Tansiyon aleti", html);
        Assert.Contains("250,50 TRY", html);
        Assert.Contains("Atatürk Cad. No:1 Kadıköy/İstanbul", html);
        Assert.Contains("Kapıda ödeme", html);
        Assert.Contains("Siparişi iptal et", html);
    }

    [Fact]
    public async Task Details_hides_cancel_once_preparing()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var html = await (await client.GetAsync($"/Patient/Orders/Details/{OrderUiData.Order2}")).Content.ReadHtmlAsync();

        Assert.Contains("Hazırlanıyor", html);
        Assert.DoesNotContain("Siparişi iptal et", html);
    }

    [Fact]
    public async Task Details_of_someone_elses_order_is_the_404_page()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var other = Guid.Parse("99999999-9999-9999-9999-999999999999");
        host.Gateway.MapStatus("GET", $"/gateway/orders/{other}", 404);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Patient/Orders/Details/{other}")).StatusCode);
    }

    [Fact]
    public async Task Cancel_posts_to_api_and_redirects_back_with_success_message()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("POST", $"/gateway/orders/{OrderUiData.Order1}/cancel", 200,
            OrderUiData.OrderJson(OrderUiData.Order1, status: "Cancelled", cancelReason: "Vazgeçtim"));

        var response = await host.PostAsync(client, $"/Patient/Orders/Cancel/{OrderUiData.Order1}", new() { ["Reason"] = "Vazgeçtim" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Patient/Orders/Details/{OrderUiData.Order1}", Location(response));
        var sent = host.Gateway.Calls("POST", $"/gateway/orders/{OrderUiData.Order1}/cancel").Single();
        Assert.Equal("Vazgeçtim", Json(sent.Body).GetProperty("reason").GetString());
        Assert.Contains("Siparişiniz iptal edildi", await (await client.GetAsync(Location(response))).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Cancel_rejected_by_api_shows_the_reason_as_warning()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.MapProblem("POST", $"/gateway/orders/{OrderUiData.Order2}/cancel", 409, "Sipariş hazırlanmaya başladığı için iptal edilemez.");

        var response = await host.PostAsync(client, $"/Patient/Orders/Cancel/{OrderUiData.Order2}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("hazırlanmaya başladığı için iptal edilemez", await (await client.GetAsync(Location(response))).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Orders_area_is_for_patients_only_and_anonymous_goes_to_login()
    {
        var (host, _) = await StartAsync();
        await using var _h = host;
        var doctor = await host.LoginAsAsync("Doctor");
        var admin = await host.LoginAsAsync("Admin");
        var anonymous = await host.CreateClient().GetAsync("/Patient/Orders");

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/Patient/Orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/Patient/Orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));
    }

    [Fact]
    public async Task Gateway_503_on_list_shows_the_service_unavailable_page()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapStatus("GET", "/gateway/orders", 503);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/Patient/Orders")).StatusCode);
    }
}

public class AdminOrdersUiTests
{
    private static async Task<(WebUiTestHost host, HttpClient client)> StartAsync(string role = "Admin", string status = "Received", bool releasePending = false)
    {
        var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/orders/{OrderUiData.Order1}", 200,
            OrderUiData.OrderJson(OrderUiData.Order1, status: status, releasePending: releasePending));
        return (host, await host.LoginAsAsync(role));
    }

    [Fact]
    public async Task List_shows_all_orders_and_passes_the_status_filter_to_the_api()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var log = host.Gateway.MapLogged("GET", "/gateway/orders", 200,
            OrderUiData.OrderPage((OrderUiData.Order1, "SIP-2026-000123", "Preparing")));

        var response = await client.GetAsync("/Admin/Orders?Status=Preparing");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("SIP-2026-000123", html);
        Assert.Contains("Hazırlanıyor", html);
        Assert.Contains("status=Preparing", log.LastQuery);
    }

    [Fact]
    public async Task List_ignores_unknown_status_values()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        var log = host.Gateway.MapLogged("GET", "/gateway/orders", 200, OrderUiData.OrderPage());

        var response = await client.GetAsync("/Admin/Orders?Status=Nonsense");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("status=", log.LastQuery);
        Assert.Contains("Sipariş bulunamadı", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Details_offers_only_the_next_status_and_cancel_for_received()
    {
        var (host, client) = await StartAsync(status: "Received");
        await using var _h = host;

        var html = await (await client.GetAsync($"/Admin/Orders/Details/{OrderUiData.Order1}")).Content.ReadHtmlAsync();

        Assert.Contains("Hazırlanıyor yap", html);
        Assert.DoesNotContain("Kargoya verildi", html);
        Assert.Contains("Siparişi iptal et", html);
        Assert.Contains("Tansiyon aleti", html);
    }

    [Fact]
    public async Task Details_of_shipped_order_has_no_cancel_and_offers_delivery()
    {
        var (host, client) = await StartAsync(status: "Shipped");
        await using var _h = host;

        var html = await (await client.GetAsync($"/Admin/Orders/Details/{OrderUiData.Order1}")).Content.ReadHtmlAsync();

        Assert.Contains("Teslim edildi", html);
        Assert.DoesNotContain("Siparişi iptal et", html);
    }

    [Fact]
    public async Task Details_warns_when_stock_release_is_pending()
    {
        var (host, client) = await StartAsync(status: "Cancelled", releasePending: true);
        await using var _h = host;

        var html = await (await client.GetAsync($"/Admin/Orders/Details/{OrderUiData.Order1}")).Content.ReadHtmlAsync();

        Assert.Contains("iade bekliyor", html);
    }

    [Fact]
    public async Task UpdateStatus_patches_the_api_and_redirects_with_success_message()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("PATCH", $"/gateway/orders/{OrderUiData.Order1}/status", 200, OrderUiData.OrderJson(OrderUiData.Order1, status: "Preparing"));

        var response = await host.PostAsync(client, $"/Admin/Orders/UpdateStatus/{OrderUiData.Order1}", new() { ["status"] = "Preparing" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Admin/Orders/Details/{OrderUiData.Order1}", Location(response));
        var sent = host.Gateway.Calls("PATCH", $"/gateway/orders/{OrderUiData.Order1}/status").Single();
        Assert.Equal("Preparing", Json(sent.Body).GetProperty("status").GetString());
        Assert.Contains("Hazırlanıyor", await (await client.GetAsync(Location(response))).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task UpdateStatus_invalid_transition_409_is_shown_as_warning()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.MapProblem("PATCH", $"/gateway/orders/{OrderUiData.Order1}/status", 409, "Sipariş 'Received' durumundan 'Delivered' durumuna geçirilemez.");

        var response = await host.PostAsync(client, $"/Admin/Orders/UpdateStatus/{OrderUiData.Order1}", new() { ["status"] = "Delivered" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("geçirilemez", await (await client.GetAsync(Location(response))).Content.ReadHtmlAsync());
    }

    [Theory]
    [InlineData("Nonsense")]
    [InlineData("Cancelled")] // iptal ayrı uçtan (neden + stok iadesi)
    public async Task UpdateStatus_with_unknown_or_cancel_target_never_reaches_the_api(string status)
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await host.PostAsync(client, $"/Admin/Orders/UpdateStatus/{OrderUiData.Order1}", new() { ["status"] = status });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    public async Task Cancel_posts_reason_and_warns_when_release_is_pending()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("POST", $"/gateway/orders/{OrderUiData.Order1}/cancel", 200,
            OrderUiData.OrderJson(OrderUiData.Order1, status: "Cancelled", releasePending: true));

        var response = await host.PostAsync(client, $"/Admin/Orders/Cancel/{OrderUiData.Order1}", new() { ["Reason"] = "Stokta yok" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var sent = host.Gateway.Calls("POST", $"/gateway/orders/{OrderUiData.Order1}/cancel").Single();
        Assert.Equal("Stokta yok", Json(sent.Body).GetProperty("reason").GetString());
        Assert.Contains("iade bekliyor", await (await client.GetAsync(Location(response))).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Admin_orders_are_for_admins_only_and_anonymous_goes_to_login()
    {
        var (host, _) = await StartAsync();
        await using var _h = host;
        host.Gateway.MapJson("GET", "/gateway/orders", 200, OrderUiData.OrderPage());
        var patient = await host.LoginAsAsync("Patient");
        var doctor = await host.LoginAsAsync("Doctor");
        var anonymous = await host.CreateClient().GetAsync("/Admin/Orders");

        Assert.Equal(HttpStatusCode.Forbidden, (await patient.GetAsync("/Admin/Orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.GetAsync("/Admin/Orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));
    }

    [Fact]
    public async Task Post_actions_require_an_antiforgery_token()
    {
        var (host, client) = await StartAsync();
        await using var _h = host;

        var response = await client.PostAsync($"/Admin/Orders/UpdateStatus/{OrderUiData.Order1}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["status"] = "Preparing" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "PATCH");
    }
}
