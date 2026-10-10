using System.Net;
using System.Text.Json;
using Hastane.WebUI.ViewModels.Shop;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Tests;

// Yalnız testlerde var olan denetleyici: üst menü sepet rozeti (view component) tek başına çizilip sınanır.
// (Rozet bileşeni burada doğrudan çağrılır.)
[Route("probe-shop")]
public class ShopProbeController : Controller
{
    [HttpGet("cart-badge"), AllowAnonymous]
    public IActionResult CartBadge() => ViewComponent("CartBadge");
}

// Mağaza (/Shop), sepet (/Cart), sepet rozeti ve Admin ürün yönetimi. Gerçek servis yok: sahte gateway.
public class ShopUiTests
{
    private static readonly Guid P1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid P2 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid P3 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    // ---------- yardımcılar ----------

    private static object Product(Guid id, string name = "C Vitamini 1000 mg", decimal price = 189.90m, int stock = 10,
        string category = "Vitamin", bool active = true, string? image = "/img/products/vitamin-c.svg", string description = "Bağışıklık desteği")
        => new { id, name, description, category, price, currency = "TRY", stock, imageUrl = image, isActive = active };

    private static object PagedProducts(object[] items, int? total = null, int page = 1, int pageSize = 12)
        => new { items, totalCount = total ?? items.Length, page, pageSize };

    private static object CartItem(Guid id, string name, decimal unit, int qty, bool available = true, int stock = 10)
        => new
        {
            productId = id,
            name,
            unitPrice = available ? unit : 0m,
            quantity = qty,
            lineTotal = available ? unit * qty : 0m,
            isAvailable = available,
            note = available ? null : "Artık satışta değil",
            stock = available ? stock : 0,
            imageUrl = "/img/products/vitamin-c.svg",
        };

    private static object Cart(params object[] items)
        => new { items, total = 0m, currency = "TRY" };

    private static object CartWithTotal(decimal total, params object[] items)
        => new { items, total, currency = "TRY" };

    private static async Task<HttpClient> LoginAsync(WebUiTestHost host, string role)
    {
        var client = host.CreateClient();
        await host.LoginAsync(client, WebUiTestHost.NewToken(role), roles: [role]);
        return client;
    }

    // Antiforgery belirteci herhangi bir sayfadan alınır; POST doğrudan hedef adrese gider.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string tokenPage, string postPath, Dictionary<string, string>? fields = null)
    {
        var html = await (await client.GetAsync(tokenPage)).Content.ReadAsStringAsync();
        var form = new Dictionary<string, string>(fields ?? [])
        {
            ["__RequestVerificationToken"] = WebUiTestHost.ExtractAntiforgeryToken(html),
        };
        return await client.PostAsync(postPath, new FormUrlEncodedContent(form));
    }

    private static string? Location(HttpResponseMessage response) => TestUrls.Relative(response.Headers.Location);

    private static RecordedRequest Last(WebUiTestHost host, string method, string path)
        => host.Gateway.Requests.Last(r => r.Method == method && r.Path == path);

    private static bool Called(WebUiTestHost host, string method, string path)
        => host.Gateway.Requests.Any(r => r.Method == method && r.Path == path);

    private static JsonElement Body(RecordedRequest request) => JsonDocument.Parse(request.Body).RootElement;

    // ---------- Mağaza: liste ----------

    [Fact]
    public async Task Shop_is_public_and_lists_products_with_categories_and_login_prompt()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/products", 200, PagedProducts([Product(P1, "C Vitamini 1000 mg", 189.90m)]));

        var response = await host.CreateClient().GetAsync("/Shop");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("C Vitamini 1000 mg", html);
        Assert.Contains("189,90 ₺", html);
        Assert.Contains($"href=\"/Shop/{P1}\"", html);
        Assert.Contains("/img/products/vitamin-c.svg", html);
        foreach (var label in new[] { "Vitamin", "Bakım", "Medikal Cihaz", "Anne-Bebek", "İlk Yardım" })
            Assert.Contains(label, html);
        // Anonim: sepete ekle düğmesi girişe götürür, ekleme formu yok.
        Assert.Contains("href=\"/Account/Login?returnUrl=%2FShop\"", html);
        Assert.DoesNotContain("/AddToCart", html);
        Assert.All(host.Gateway.Requests.Where(r => r.Path == "/gateway/products"), r => Assert.Equal(string.Empty, r.Authorization));
    }

    [Fact]
    public async Task Shop_forwards_search_category_and_page_to_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var log = host.Gateway.MapLogged("GET", "/gateway/products", 200, PagedProducts([Product(P1)], total: 30, page: 2));

        var response = await host.CreateClient().GetAsync("/Shop?q=vitamin&category=Vitamin&page=2");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("search=vitamin", log.LastQuery);
        Assert.Contains("category=Vitamin", log.LastQuery);
        Assert.Contains("page=2", log.LastQuery);
        Assert.Contains("pageSize=12", log.LastQuery);
        Assert.Contains("2 / 3", html); // 30 ürün, sayfa başına 12 -> 3 sayfa
        Assert.Contains("category=Vitamin", html); // sayfa bağlantıları süzgeci korur
    }

    [Theory]
    [InlineData("/Shop?category=Foo")]
    [InlineData("/Shop?category=1")]
    [InlineData("/Shop?page=-5")]
    [InlineData("/Shop?page=abc")]
    public async Task Shop_ignores_unknown_category_and_bad_page_instead_of_sending_a_bad_request(string url)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var log = host.Gateway.MapLogged("GET", "/gateway/products", 200, PagedProducts([Product(P1)]));

        var response = await host.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("category=", log.LastQuery);
        Assert.Contains("page=1", log.LastQuery);
    }

    [Fact]
    public async Task Shop_shows_empty_state_when_nothing_matches()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/products", 200, PagedProducts([]));

        var html = await (await host.CreateClient().GetAsync("/Shop?q=yok")).Content.ReadHtmlAsync();

        Assert.Contains("Aramanıza uygun ürün bulunamadı.", html);
    }

    [Fact]
    public async Task Shop_shows_503_page_when_market_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/products", 503);

        var response = await host.CreateClient().GetAsync("/Shop");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Servis şu an yanıt vermiyor", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Shop_encodes_product_text_and_drops_unsafe_image_urls()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/products", 200,
            PagedProducts([Product(P1, "<script>alert(1)</script>", image: "javascript:alert(1)")]));

        var raw = await (await host.CreateClient().GetAsync("/Shop")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)</script>", raw);
        Assert.DoesNotContain("javascript:alert", raw);
        Assert.Contains("/img/products/placeholder.svg", raw);
    }

    [Fact]
    public async Task Shop_marks_low_and_zero_stock_and_hides_add_for_zero_stock()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/products", 200, PagedProducts([Product(P1, "Az Kalan", stock: 3), Product(P2, "Biten", stock: 0)]));
        var client = await LoginAsync(host, "Patient");

        var html = await (await client.GetAsync("/Shop")).Content.ReadHtmlAsync();

        Assert.Contains("Son 3 adet", html);
        Assert.Contains("Stokta yok", html);
        Assert.Contains($"/Shop/{P1}/AddToCart", html);
        Assert.DoesNotContain($"/Shop/{P2}/AddToCart", html);
    }

    [Fact]
    public async Task Shop_for_patient_has_add_to_cart_form_with_antiforgery_and_bearer_header()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/products", 200, PagedProducts([Product(P1)]));
        var client = await LoginAsync(host, "Patient");

        var html = await (await client.GetAsync("/Shop")).Content.ReadHtmlAsync();

        Assert.Contains($"action=\"/Shop/{P1}/AddToCart\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.StartsWith("Bearer ", Last(host, "GET", "/gateway/products").Authorization);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Doctor")]
    public async Task Shop_for_admin_and_doctor_shows_products_but_no_buy_button(string role)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/products", 200, PagedProducts([Product(P1)]));
        var client = await LoginAsync(host, role);

        var html = await (await client.GetAsync("/Shop")).Content.ReadHtmlAsync();

        Assert.Contains("C Vitamini 1000 mg", html);
        Assert.DoesNotContain("/AddToCart", html);
        Assert.DoesNotContain("Sepete ekle", html);
    }

    // ---------- Mağaza: ürün ayrıntı ----------

    [Fact]
    public async Task Details_is_public_and_asks_anonymous_visitors_to_sign_in()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/products/{P1}", 200, Product(P1, "C Vitamini 1000 mg", 189.90m, description: "Günde bir tablet."));

        var response = await host.CreateClient().GetAsync($"/Shop/{P1}");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("C Vitamini 1000 mg", html);
        Assert.Contains("Günde bir tablet.", html);
        Assert.Contains("189,90 ₺", html);
        Assert.Contains($"returnUrl=%2FShop%2F{P1}", html);
        Assert.DoesNotContain("name=\"quantity\"", html);
    }

    [Fact]
    public async Task Details_for_patient_offers_quantities_up_to_stock_and_twenty()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/products/{P1}", 200, Product(P1, stock: 3));
        host.Gateway.MapJson("GET", $"/gateway/products/{P2}", 200, Product(P2, stock: 500));
        var client = await LoginAsync(host, "Patient");

        var small = await (await client.GetAsync($"/Shop/{P1}")).Content.ReadHtmlAsync();
        var large = await (await client.GetAsync($"/Shop/{P2}")).Content.ReadHtmlAsync();

        Assert.Contains("<option value=\"3\">3</option>", small);
        Assert.DoesNotContain("<option value=\"4\">", small);
        Assert.Contains("<option value=\"20\">20</option>", large);
        Assert.DoesNotContain("<option value=\"21\">", large);
        Assert.Contains($"action=\"/Shop/{P1}/AddToCart\"", small);
    }

    [Fact]
    public async Task Details_for_out_of_stock_product_has_no_buy_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/products/{P1}", 200, Product(P1, stock: 0));
        var client = await LoginAsync(host, "Patient");

        var html = await (await client.GetAsync($"/Shop/{P1}")).Content.ReadHtmlAsync();

        Assert.Contains("Stokta yok", html);
        Assert.DoesNotContain("/AddToCart", html);
    }

    [Fact]
    public async Task Details_for_unknown_or_inactive_product_shows_404_page()
    {
        await using var host = await WebUiTestHost.StartAsync(); // tanımsız yol -> sahte gateway 404

        var response = await host.CreateClient().GetAsync($"/Shop/{P3}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Details_with_non_guid_id_is_404_not_500()
    {
        await using var host = await WebUiTestHost.StartAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await host.CreateClient().GetAsync("/Shop/abc")).StatusCode);
    }

    // ---------- Sepete ekle ----------

    [Fact]
    public async Task AddToCart_adds_to_the_existing_quantity_and_redirects_back_with_a_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart(CartItem(P1, "C Vitamini", 189.90m, 2)));
        host.Gateway.MapJson("PUT", $"/gateway/cart/items/{P1}", 200, Cart(CartItem(P1, "C Vitamini", 189.90m, 5)));
        host.Gateway.MapJson("GET", $"/gateway/products/{P1}", 200, Product(P1));
        var client = await LoginAsync(host, "Patient");

        var response = await PostAsync(client, "/Home/About", $"/Shop/{P1}/AddToCart", new() { ["quantity"] = "3" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/Shop/{P1}", Location(response));
        var put = Last(host, "PUT", $"/gateway/cart/items/{P1}");
        Assert.Equal(5, Body(put).GetProperty("quantity").GetInt32()); // mevcut 2 + 3
        Assert.StartsWith("Bearer ", put.Authorization);
        Assert.Contains("Ürün sepete eklendi.", await (await client.GetAsync($"/Shop/{P1}")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task AddToCart_caps_at_twenty_and_does_not_call_put_when_already_at_the_limit()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart(CartItem(P1, "C Vitamini", 189.90m, 20, stock: 50)));
        host.Gateway.MapJson("GET", $"/gateway/products/{P1}", 200, Product(P1, stock: 50));
        var client = await LoginAsync(host, "Patient");

        var response = await PostAsync(client, "/Home/About", $"/Shop/{P1}/AddToCart", new() { ["quantity"] = "5" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(Called(host, "PUT", $"/gateway/cart/items/{P1}"));
        Assert.Contains("en fazla (20) adet", await (await client.GetAsync($"/Shop/{P1}")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task AddToCart_clamps_out_of_range_quantity_to_one_to_twenty()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart());
        host.Gateway.MapJson("PUT", $"/gateway/cart/items/{P1}", 200, Cart(CartItem(P1, "C", 1m, 1)));
        var client = await LoginAsync(host, "Patient");

        await PostAsync(client, "/Home/About", $"/Shop/{P1}/AddToCart", new() { ["quantity"] = "-7" });
        Assert.Equal(1, Body(Last(host, "PUT", $"/gateway/cart/items/{P1}")).GetProperty("quantity").GetInt32());

        await PostAsync(client, "/Home/About", $"/Shop/{P1}/AddToCart", new() { ["quantity"] = "999" });
        Assert.Equal(20, Body(Last(host, "PUT", $"/gateway/cart/items/{P1}")).GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task AddToCart_shows_the_api_message_when_stock_is_not_enough()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart());
        host.MapProblem("PUT", $"/gateway/cart/items/{P1}", 409, "Yetersiz stok: en fazla 2 adet eklenebilir.");
        host.Gateway.MapJson("GET", $"/gateway/products/{P1}", 200, Product(P1, stock: 2));
        var client = await LoginAsync(host, "Patient");

        var response = await PostAsync(client, "/Home/About", $"/Shop/{P1}/AddToCart", new() { ["quantity"] = "5" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Yetersiz stok: en fazla 2 adet eklenebilir.", await (await client.GetAsync($"/Shop/{P1}")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task AddToCart_for_a_product_that_left_the_shop_shows_a_warning_not_an_error_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart());
        host.Gateway.MapStatus("PUT", $"/gateway/cart/items/{P1}", 404);
        host.Gateway.MapJson("GET", "/gateway/products", 200, PagedProducts([]));
        var client = await LoginAsync(host, "Patient");

        var response = await PostAsync(client, "/Home/About", $"/Shop/{P1}/AddToCart", new() { ["quantity"] = "1", ["returnUrl"] = "/Shop" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Shop", Location(response));
        Assert.Contains("Bu ürün artık satışta değil.", await (await client.GetAsync("/Shop")).Content.ReadHtmlAsync());
    }

    [Theory]
    [InlineData("https://evil.example/phish", "none")]
    [InlineData("//evil.example/phish", "none")]
    [InlineData("/Shop?category=Care", "/Shop?category=Care")]
    public async Task AddToCart_return_url_must_be_local(string returnUrl, string expected)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart());
        host.Gateway.MapJson("PUT", $"/gateway/cart/items/{P1}", 200, Cart(CartItem(P1, "C", 1m, 1)));
        var client = await LoginAsync(host, "Patient");

        var response = await PostAsync(client, "/Home/About", $"/Shop/{P1}/AddToCart", new() { ["quantity"] = "1", ["returnUrl"] = returnUrl });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected == "none" ? $"/Shop/{P1}" : expected, Location(response));
    }

    [Fact]
    public async Task AddToCart_requires_login_and_the_patient_role()
    {
        await using var host = await WebUiTestHost.StartAsync();

        // Anonim: girişe yönlendirilir (antiforgery belirteci giriş sayfasından), servise hiç gidilmez.
        var anonymous = host.CreateClient();
        var anonymousResponse = await PostAsync(anonymous, "/Account/Login", $"/Shop/{P1}/AddToCart", new() { ["quantity"] = "1" });
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymousResponse));

        // Admin ve Doctor: 403.
        foreach (var role in new[] { "Admin", "Doctor" })
        {
            var client = await LoginAsync(host, role);
            var response = await PostAsync(client, "/Home/About", $"/Shop/{P1}/AddToCart", new() { ["quantity"] = "1" });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/cart", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddToCart_without_antiforgery_token_is_400()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await LoginAsync(host, "Patient");

        var response = await client.PostAsync($"/Shop/{P1}/AddToCart", new FormUrlEncodedContent(new Dictionary<string, string> { ["quantity"] = "1" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method == "PUT");
    }

    // ---------- Sepetim ----------

    [Fact]
    public async Task Cart_requires_the_patient_role()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var anonymous = await host.CreateClient().GetAsync("/Cart");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));

        foreach (var role in new[] { "Admin", "Doctor" })
        {
            var client = await LoginAsync(host, role);
            var response = await client.GetAsync("/Cart");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("Yetkiniz yok", await response.Content.ReadHtmlAsync());
        }

        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/cart");
    }

    [Fact]
    public async Task Cart_lists_items_total_and_links_to_checkout()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, CartWithTotal(299.90m,
            CartItem(P1, "C Vitamini", 100m, 2),
            CartItem(P2, "Ateş Ölçer", 99.90m, 1)));
        var client = await LoginAsync(host, "Patient");

        var response = await client.GetAsync("/Cart");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("C Vitamini", html);
        Assert.Contains("Ateş Ölçer", html);
        Assert.Contains("200,00 ₺", html);   // satır tutarı
        Assert.Contains("299,90 ₺", html);   // API'nin verdiği toplam (arayüz kendisi toplamaz)
        Assert.Contains("href=\"/Checkout\"", html);
        Assert.Contains("Siparişi tamamla", html);
        Assert.Contains("action=\"/Cart/Update\"", html);
        Assert.Contains("action=\"/Cart/Remove\"", html);
        Assert.StartsWith("Bearer ", Last(host, "GET", "/gateway/cart").Authorization);
    }

    [Fact]
    public async Task Cart_marks_unavailable_items_and_warns_about_stock()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, CartWithTotal(100m,
            CartItem(P1, "Az Stoklu", 20m, 5, stock: 3),
            CartItem(P2, "Pasife Alınan", 50m, 1, available: false)));
        var client = await LoginAsync(host, "Patient");

        var html = await (await client.GetAsync("/Cart")).Content.ReadHtmlAsync();

        Assert.Contains("Artık satışta değil", html);
        Assert.Contains("Pasife Alınan", html);
        Assert.Contains("Stokta yalnız 3 adet var.", html);
        Assert.Contains("Siparişi tamamlamadan önce adetleri düzeltin", html);
        // Satışta olmayan ürünün yalnız "kaldır" düğmesi var, adet formu yok.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "action=\"/Cart/Update\""));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "action=\"/Cart/Remove\"").Count);
    }

    [Fact]
    public async Task Cart_with_only_unavailable_items_disables_checkout()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart(CartItem(P1, "Pasife Alınan", 50m, 1, available: false)));
        var client = await LoginAsync(host, "Patient");

        var html = await (await client.GetAsync("/Cart")).Content.ReadHtmlAsync();

        Assert.DoesNotContain("href=\"/Checkout\"", html);
        Assert.Contains("disabled", html);
    }

    [Fact]
    public async Task Cart_empty_shows_empty_state_with_link_to_shop()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart());
        var client = await LoginAsync(host, "Patient");

        var html = await (await client.GetAsync("/Cart")).Content.ReadHtmlAsync();

        Assert.Contains("Sepetiniz boş.", html);
        Assert.Contains("href=\"/Shop\"", html);
        Assert.DoesNotContain("href=\"/Checkout\"", html);
    }

    [Fact]
    public async Task Cart_shows_503_page_when_the_cart_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/cart", 503);
        var client = await LoginAsync(host, "Patient");

        var response = await client.GetAsync("/Cart");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Cart_update_sends_the_absolute_quantity_and_redirects_to_cart()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("PUT", $"/gateway/cart/items/{P1}", 200, Cart(CartItem(P1, "C", 1m, 4)));
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart(CartItem(P1, "C Vitamini", 189.90m, 4)));
        var client = await LoginAsync(host, "Patient");

        var response = await PostAsync(client, "/Cart", "/Cart/Update", new() { ["productId"] = P1.ToString(), ["quantity"] = "4" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Cart", Location(response));
        Assert.Equal(4, Body(Last(host, "PUT", $"/gateway/cart/items/{P1}")).GetProperty("quantity").GetInt32());
        Assert.Contains("Sepet güncellendi.", await (await client.GetAsync("/Cart")).Content.ReadHtmlAsync());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("21")]
    [InlineData("1000")]
    public async Task Cart_update_with_out_of_range_quantity_never_reaches_the_service(string quantity)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart(CartItem(P1, "C Vitamini", 189.90m, 2)));
        var client = await LoginAsync(host, "Patient");

        var response = await PostAsync(client, "/Cart", "/Cart/Update", new() { ["productId"] = P1.ToString(), ["quantity"] = quantity });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(Called(host, "PUT", $"/gateway/cart/items/{P1}"));
        Assert.Contains("Adet 1 ile 20 arasında olmalı.", await (await client.GetAsync("/Cart")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Cart_update_shows_the_api_message_on_stock_conflict()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.MapProblem("PUT", $"/gateway/cart/items/{P1}", 409, "Yetersiz stok: en fazla 3 adet eklenebilir.");
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart(CartItem(P1, "C Vitamini", 189.90m, 2)));
        var client = await LoginAsync(host, "Patient");

        await PostAsync(client, "/Cart", "/Cart/Update", new() { ["productId"] = P1.ToString(), ["quantity"] = "9" });

        Assert.Contains("Yetersiz stok: en fazla 3 adet eklenebilir.", await (await client.GetAsync("/Cart")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Cart_remove_sends_quantity_zero()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("PUT", $"/gateway/cart/items/{P1}", 200, Cart());
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart());
        var client = await LoginAsync(host, "Patient");

        var response = await PostAsync(client, "/Cart", "/Cart/Remove", new() { ["productId"] = P1.ToString() });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, Body(Last(host, "PUT", $"/gateway/cart/items/{P1}")).GetProperty("quantity").GetInt32());
        Assert.Contains("Ürün sepetten çıkarıldı.", await (await client.GetAsync("/Cart")).Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Cart_clear_calls_delete()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("DELETE", "/gateway/cart", 204);
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart());
        var client = await LoginAsync(host, "Patient");

        var response = await PostAsync(client, "/Cart", "/Cart/Clear");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(Called(host, "DELETE", "/gateway/cart"));
        Assert.Contains("Sepet boşaltıldı.", await (await client.GetAsync("/Cart")).Content.ReadHtmlAsync());
    }

    [Theory]
    [InlineData("/Cart/Update")]
    [InlineData("/Cart/Remove")]
    [InlineData("/Cart/Clear")]
    public async Task Cart_posts_require_antiforgery_and_never_write_without_it(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await LoginAsync(host, "Patient");

        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["productId"] = P1.ToString(), ["quantity"] = "1" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method is "PUT" or "DELETE" && r.Path.StartsWith("/gateway/cart", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/Cart/Update")]
    [InlineData("/Cart/Remove")]
    [InlineData("/Cart/Clear")]
    public async Task Cart_posts_are_403_for_other_roles(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await LoginAsync(host, "Admin");

        var response = await PostAsync(client, "/Home/About", path, new() { ["productId"] = P1.ToString(), ["quantity"] = "1" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/cart", StringComparison.Ordinal));
    }

    // ---------- Üst menü sepet rozeti (view component) ----------

    [Fact]
    public async Task CartBadge_shows_total_quantity_of_available_items_for_patient()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart(
            CartItem(P1, "A", 10m, 2), CartItem(P2, "B", 10m, 3), CartItem(P3, "Pasif", 10m, 4, available: false)));
        var client = await LoginAsync(host, "Patient");

        var html = await (await client.GetAsync("/probe-shop/cart-badge")).Content.ReadHtmlAsync();

        Assert.Contains("href=\"/Cart\"", html);
        Assert.Contains("bi-cart3", html);
        Assert.Contains("cart-count", html);
        Assert.Contains(">5<", html); // 2 + 3; satışta olmayan sayılmaz
    }

    [Fact]
    public async Task CartBadge_is_hidden_for_empty_cart_but_keeps_the_icon()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/cart", 200, Cart());
        var client = await LoginAsync(host, "Patient");

        var html = await (await client.GetAsync("/probe-shop/cart-badge")).Content.ReadHtmlAsync();

        Assert.Contains("bi-cart3", html);
        Assert.DoesNotContain("cart-count", html);
    }

    [Fact]
    public async Task CartBadge_does_not_break_the_page_when_the_cart_service_is_down()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", "/gateway/cart", 503);
        var client = await LoginAsync(host, "Patient");

        var response = await client.GetAsync("/probe-shop/cart-badge");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("bi-cart3", html);
        Assert.DoesNotContain("cart-count", html);
    }

    [Fact]
    public async Task CartBadge_renders_nothing_for_anonymous_and_non_patients_and_asks_no_questions()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var anonymous = await host.CreateClient().GetAsync("/probe-shop/cart-badge");
        Assert.Equal(string.Empty, (await anonymous.Content.ReadAsStringAsync()).Trim());

        foreach (var role in new[] { "Admin", "Doctor" })
        {
            var client = await LoginAsync(host, role);
            Assert.Equal(string.Empty, (await (await client.GetAsync("/probe-shop/cart-badge")).Content.ReadAsStringAsync()).Trim());
        }

        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path == "/gateway/cart");
    }

    // ---------- Admin ürünler ----------

    [Fact]
    public async Task Admin_products_require_the_admin_role()
    {
        await using var host = await WebUiTestHost.StartAsync();

        var anonymous = await host.CreateClient().GetAsync("/Admin/Products");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/Account/Login", Location(anonymous));

        foreach (var role in new[] { "Patient", "Doctor" })
        {
            var client = await LoginAsync(host, role);
            foreach (var path in new[] { "/Admin/Products", "/Admin/Products/Create", $"/Admin/Products/Edit/{P1}", $"/Admin/Products/Delete/{P1}" })
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }

        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/products", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admin_products_list_asks_for_inactive_products_too_and_shows_status_and_stock()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var log = host.Gateway.MapLogged("GET", "/gateway/products", 200, PagedProducts(
            [Product(P1, "Satıştaki", stock: 5), Product(P2, "Pasif Ürün", active: false, stock: 0)]));
        var client = await LoginAsync(host, "Admin");

        var response = await client.GetAsync("/Admin/Products?q=urun&category=Care");
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("includeInactive=true", log.LastQuery);
        Assert.Contains("category=Care", log.LastQuery);
        Assert.Contains("Satıştaki", html);
        Assert.Contains("Pasif Ürün", html);
        Assert.Contains("Pasif", html);
        Assert.Contains("Satışta", html);
        Assert.Contains($"href=\"/Admin/Products/Edit/{P1}\"", html);
        Assert.Contains($"href=\"/Admin/Products/Delete/{P2}\"", html);
        Assert.StartsWith("Bearer ", Last(host, "GET", "/gateway/products").Authorization);
    }

    [Fact]
    public async Task Admin_create_form_lists_the_five_fixed_categories()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await LoginAsync(host, "Admin");

        var html = await (await client.GetAsync("/Admin/Products/Create")).Content.ReadHtmlAsync();

        foreach (var key in new[] { "Vitamin", "Care", "MedicalDevice", "MotherBaby", "FirstAid" })
            Assert.Contains($"<option value=\"{key}\"", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task Admin_create_posts_a_clean_body_and_redirects_to_the_list()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/products", 201, Product(P1));
        var client = await LoginAsync(host, "Admin");

        var response = await PostAsync(client, "/Admin/Products/Create", "/Admin/Products/Create", new()
        {
            ["Name"] = "  Yeni Ürün  ",
            ["Description"] = "Açıklama",
            ["Category"] = "MedicalDevice",
            ["Price"] = "1149,90",
            ["Stock"] = "12",
            ["ImageUrl"] = "/img/products/thermometer.svg",
            ["IsActive"] = "true",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Products", Location(response));
        var body = Body(Last(host, "POST", "/gateway/products"));
        Assert.Equal("Yeni Ürün", body.GetProperty("name").GetString());
        Assert.Equal("MedicalDevice", body.GetProperty("category").GetString());
        Assert.Equal(1149.90m, body.GetProperty("price").GetDecimal());
        Assert.Equal(12, body.GetProperty("stock").GetInt32());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal("/img/products/thermometer.svg", body.GetProperty("imageUrl").GetString());
        Assert.False(body.TryGetProperty("currency", out _));
        Assert.False(body.TryGetProperty("id", out _));
    }

    [Theory]
    [InlineData("149.90", 149.90)]
    [InlineData("149,9", 149.9)]
    [InlineData("10", 10)]
    public async Task Admin_create_accepts_dot_or_comma_prices(string price, double expected)
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/products", 201, Product(P1));
        var client = await LoginAsync(host, "Admin");

        await PostAsync(client, "/Admin/Products/Create", "/Admin/Products/Create", new()
        {
            ["Name"] = "Ürün",
            ["Category"] = "Vitamin",
            ["Price"] = price,
            ["Stock"] = "1",
            ["IsActive"] = "true",
        });

        Assert.Equal((decimal)expected, Body(Last(host, "POST", "/gateway/products")).GetProperty("price").GetDecimal());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10,123")]
    [InlineData("1000001")]
    public async Task Admin_create_with_invalid_price_redisplays_the_form_without_calling_the_service(string price)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await LoginAsync(host, "Admin");

        var response = await PostAsync(client, "/Admin/Products/Create", "/Admin/Products/Create", new()
        {
            ["Name"] = "Ürün",
            ["Category"] = "Vitamin",
            ["Price"] = price,
            ["Stock"] = "1",
            ["IsActive"] = "true",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ürün ekle", await response.Content.ReadHtmlAsync());
        Assert.False(Called(host, "POST", "/gateway/products"));
    }

    [Fact]
    public async Task Admin_create_rejects_unknown_category_unsafe_image_and_negative_stock_locally()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await LoginAsync(host, "Admin");

        var response = await PostAsync(client, "/Admin/Products/Create", "/Admin/Products/Create", new()
        {
            ["Name"] = "Ürün",
            ["Category"] = "Foo",
            ["Price"] = "10",
            ["Stock"] = "-1",
            ["ImageUrl"] = "javascript:alert(1)",
            ["IsActive"] = "true",
        });
        var html = await response.Content.ReadHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Stok 0 ile 100000 arasında olmalı.", html);
        Assert.False(Called(host, "POST", "/gateway/products"));

        // Stok geçerli olunca model düzeyi kurallar (kategori, görsel) da devreye girer.
        var second = await PostAsync(client, "/Admin/Products/Create", "/Admin/Products/Create", new()
        {
            ["Name"] = "Ürün",
            ["Category"] = "Foo",
            ["Price"] = "10",
            ["Stock"] = "1",
            ["ImageUrl"] = "javascript:alert(1)",
            ["IsActive"] = "true",
        });
        var secondHtml = await second.Content.ReadHtmlAsync();
        Assert.Contains("Geçerli bir kategori seçin.", secondHtml);
        Assert.Contains("Görsel adresi", secondHtml);
        Assert.False(Called(host, "POST", "/gateway/products"));
    }

    [Fact]
    public async Task Admin_create_shows_field_errors_returned_by_the_api()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/products", 400, new
        {
            status = 400,
            title = "Doğrulama hatası",
            errors = new Dictionary<string, string[]> { ["Name"] = ["Bu ad zaten kullanılıyor."] },
        });
        var client = await LoginAsync(host, "Admin");

        var response = await PostAsync(client, "/Admin/Products/Create", "/Admin/Products/Create", new()
        {
            ["Name"] = "Ürün",
            ["Category"] = "Vitamin",
            ["Price"] = "10",
            ["Stock"] = "1",
            ["IsActive"] = "true",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bu ad zaten kullanılıyor.", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Admin_edit_prefills_the_form_and_put_sends_the_changes()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/products/{P1}", 200, Product(P1, "Eski Ad", 149.90m, stock: 7, category: "Care", active: false));
        host.Gateway.MapJson("PUT", $"/gateway/products/{P1}", 200, Product(P1));
        var client = await LoginAsync(host, "Admin");

        var html = await (await client.GetAsync($"/Admin/Products/Edit/{P1}")).Content.ReadHtmlAsync();
        Assert.Contains("value=\"Eski Ad\"", html);
        Assert.Contains("value=\"149.90\"", html);
        Assert.Contains("value=\"7\"", html);
        Assert.Matches("<option value=\"Care\"[^>]*selected", html);

        var response = await PostAsync(client, $"/Admin/Products/Edit/{P1}", $"/Admin/Products/Edit/{P1}", new()
        {
            ["Name"] = "Yeni Ad",
            ["Category"] = "Care",
            ["Price"] = "155,50",
            ["Stock"] = "20",
            ["IsActive"] = "true",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Products", Location(response));
        var body = Body(Last(host, "PUT", $"/gateway/products/{P1}"));
        Assert.Equal("Yeni Ad", body.GetProperty("name").GetString());
        Assert.Equal(155.50m, body.GetProperty("price").GetDecimal());
        Assert.Equal(20, body.GetProperty("stock").GetInt32());
    }

    [Fact]
    public async Task Admin_edit_shows_the_api_message_when_stock_changed_meanwhile()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/products/{P1}", 200, Product(P1));
        host.MapProblem("PUT", $"/gateway/products/{P1}", 409, "Ürün başka bir işlemle değişti (stok düşmüş olabilir). Sayfayı yenileyip tekrar deneyin.");
        var client = await LoginAsync(host, "Admin");

        var response = await PostAsync(client, $"/Admin/Products/Edit/{P1}", $"/Admin/Products/Edit/{P1}", new()
        {
            ["Name"] = "Ad",
            ["Category"] = "Vitamin",
            ["Price"] = "10",
            ["Stock"] = "5",
            ["IsActive"] = "true",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("stok düşmüş olabilir", await response.Content.ReadHtmlAsync());
    }

    [Fact]
    public async Task Admin_edit_of_unknown_product_is_404()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await LoginAsync(host, "Admin");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Admin/Products/Edit/{P3}")).StatusCode);
    }

    [Fact]
    public async Task Admin_delete_get_only_confirms_and_post_deletes()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/products/{P1}", 200, Product(P1, "Silinecek Ürün"));
        host.Gateway.MapStatus("DELETE", $"/gateway/products/{P1}", 204);
        var client = await LoginAsync(host, "Admin");

        var page = await client.GetAsync($"/Admin/Products/Delete/{P1}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadHtmlAsync();
        Assert.Contains("Silinecek Ürün", html);
        Assert.Contains("method=\"post\"", html);
        Assert.False(Called(host, "DELETE", $"/gateway/products/{P1}")); // GET silmez

        var response = await PostAsync(client, $"/Admin/Products/Delete/{P1}", $"/Admin/Products/Delete/{P1}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin/Products", Location(response));
        Assert.True(Called(host, "DELETE", $"/gateway/products/{P1}"));
    }

    [Theory]
    [InlineData("/Admin/Products/Create")]
    [InlineData("/Admin/Products/Edit/aaaaaaaa-0000-0000-0000-000000000001")]
    [InlineData("/Admin/Products/Delete/aaaaaaaa-0000-0000-0000-000000000001")]
    public async Task Admin_product_posts_require_antiforgery(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await LoginAsync(host, "Admin");

        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Ürün",
            ["Category"] = "Vitamin",
            ["Price"] = "10",
            ["Stock"] = "1",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method is "POST" or "PUT" or "DELETE" && r.Path.StartsWith("/gateway/products", StringComparison.Ordinal));
    }

    // ---------- Saf mantık ----------

    [Theory]
    [InlineData(189.9, "TRY", "189,90 ₺")]
    [InlineData(0.5, "TRY", "0,50 ₺")]
    [InlineData(1234567.5, "TRY", "1.234.567,50 ₺")]
    [InlineData(5, "USD", "5,00 USD")]
    public void Money_uses_turkish_format_regardless_of_server_culture(double amount, string currency, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Assert.Equal(expected, ShopDisplay.Money((decimal)amount, currency));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("149,90", "149.90")]
    [InlineData("149.90", "149.90")]
    [InlineData(" 10 ", "10")]
    [InlineData("0,01", "0.01")]
    [InlineData("abc", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("1,234", null)]
    [InlineData("1.234,50", null)]
    public void ParsePrice_accepts_dot_or_comma_with_at_most_two_decimals(string? text, string? expected)
        => Assert.Equal(expected is null ? null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), ProductFormViewModel.ParsePrice(text));

    [Theory]
    [InlineData("vitamin", "Vitamin")]
    [InlineData(" MEDICALDEVICE ", "MedicalDevice")]
    [InlineData("FirstAid", "FirstAid")]
    [InlineData("1", null)]
    [InlineData("Foo", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeCategory_accepts_only_the_five_fixed_names(string? input, string? expected)
        => Assert.Equal(expected, ShopDisplay.NormalizeCategory(input));

    [Theory]
    [InlineData(0, "Stokta yok")]
    [InlineData(-1, "Stokta yok")]
    [InlineData(1, "Son 1 adet")]
    [InlineData(5, "Son 5 adet")]
    [InlineData(6, null)]
    public void StockNote_marks_low_stock(int stock, string? expected) => Assert.Equal(expected, ShopDisplay.StockNote(stock));

    [Fact]
    public void CleanSearch_trims_and_caps_length()
    {
        Assert.Null(ShopDisplay.CleanSearch("   "));
        Assert.Equal("vitamin", ShopDisplay.CleanSearch("  vitamin "));
        Assert.Equal(100, ShopDisplay.CleanSearch(new string('a', 500))!.Length);
    }

    [Fact]
    public void Category_labels_are_turkish_and_complete()
    {
        Assert.Equal(new[] { "Vitamin", "Bakım", "Medikal Cihaz", "Anne-Bebek", "İlk Yardım" }, ShopDisplay.Categories.Select(c => c.Label));
        Assert.Equal("Bakım", ShopDisplay.CategoryLabel("Care"));
        Assert.Equal("Bilinmeyen", ShopDisplay.CategoryLabel("Bilinmeyen"));
    }
}
