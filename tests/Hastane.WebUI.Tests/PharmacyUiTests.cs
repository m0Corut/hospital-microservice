using System.Net;
using static Hastane.WebUI.Tests.AdminPanelTestHelpers;

namespace Hastane.WebUI.Tests;

// Admin "İlaçlar" ve "Reçete Karşıla" ekranları. Gerçek servis yok: sahte gateway yanıt verir (UI-KURALLARI.md §7).
internal static class PharmacyUiTestData
{
    public static readonly Guid MedicineId = Guid.Parse("aaaaaaaa-0000-7000-8000-000000000001");
    public static readonly Guid OtherMedicineId = Guid.Parse("aaaaaaaa-0000-7000-8000-000000000002");
    public static readonly Guid RxId = Guid.Parse("bbbbbbbb-0000-7000-8000-000000000001");

    public static object Medicine(Guid? id = null, string name = "Parol", string form = "Tablet", int stock = 40, int minStock = 10, bool low = false)
        => new
        {
            id = id ?? MedicineId,
            name,
            activeIngredient = "Parasetamol",
            form,
            strength = "500 mg",
            barcode = "8690000000001",
            price = 25.5m,
            currency = "TRY",
            stock,
            minStock,
            isLowStock = low,
            requiresPrescription = false,
            isActive = true,
            createdAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
            updatedAt = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc),
            rowVersion = "AAAAAAAAB9E=",
        };

    public static object Movement(string type = "Adjustment", int delta = -2, int after = 38, string reason = "fire", Guid? rx = null)
        => new
        {
            id = Guid.NewGuid(),
            medicineId = MedicineId,
            type,
            delta,
            stockAfter = after,
            reason,
            prescriptionId = rx,
            createdBy = "admin-1",
            createdAt = new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc),
        };

    public static object Line(int no, string rxName, string status, string? medicine = null, int required = 1, int available = 5)
        => new
        {
            lineNo = no,
            prescriptionMedicineName = rxName,
            medicineId = medicine is null ? (Guid?)null : MedicineId,
            medicineName = medicine,
            required,
            available,
            status,
        };

    public static object Preview(bool can, bool already, params object[] lines) => new
    {
        prescriptionId = RxId,
        patientId = "pat-1",
        doctorId = "doc-1",
        alreadyDispensed = already,
        canDispense = can,
        lines,
    };

    public static object Dispensation(Guid? rx = null) => new
    {
        id = Guid.NewGuid(),
        prescriptionId = rx ?? RxId,
        patientId = "pat-1",
        doctorId = "doc-1",
        dispensedBy = "admin-1",
        dispensedAt = new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc),
        currency = "TRY",
        total = 51.0m,
        lines = new[]
        {
            new { lineNo = 1, prescriptionMedicineName = "parol", medicineId = MedicineId, medicineName = "Parol", quantity = 1, unitPrice = 25.5m },
            new { lineNo = 2, prescriptionMedicineName = "aspirin", medicineId = OtherMedicineId, medicineName = "Aspirin", quantity = 1, unitPrice = 25.5m },
        },
    };

    public static Dictionary<string, string> ValidForm() => new()
    {
        ["Name"] = "Yeni İlaç",
        ["ActiveIngredient"] = "Etken",
        ["Form"] = "Syrup",
        ["Strength"] = "5 ml",
        ["Barcode"] = "8691234567890",
        ["Price"] = "25,50",
        ["Stock"] = "12",
        ["MinStock"] = "3",
    };
}

public class PharmacyAccessTests
{
    public static TheoryData<string> PharmacyPages() => new()
    {
        "/Admin/PharmacyMedicines",
        "/Admin/PharmacyMedicines/Create",
        $"/Admin/PharmacyMedicines/Edit/{PharmacyUiTestData.MedicineId}",
        $"/Admin/PharmacyMedicines/Delete/{PharmacyUiTestData.MedicineId}",
        $"/Admin/PharmacyMedicines/Stock/{PharmacyUiTestData.MedicineId}",
        $"/Admin/PharmacyMedicines/Movements/{PharmacyUiTestData.MedicineId}",
        "/Admin/PharmacyDispensing",
        "/Admin/PharmacyDispensing/Dispensations",
    };

    [Theory]
    [MemberData(nameof(PharmacyPages))]
    public async Task Anonymous_is_redirected_to_login(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();

        var response = await host.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", Location(response));
        Assert.Empty(host.Gateway.Requests);
    }

    [Theory]
    [MemberData(nameof(PharmacyPages))]
    public async Task Patient_and_doctor_get_403_and_no_gateway_call(string path)
    {
        foreach (var role in new[] { "Patient", "Doctor" })
        {
            await using var host = await WebUiTestHost.StartAsync();
            var client = await ClientAsync(host, role);
            var before = host.Gateway.Requests.Count;

            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("Yetkiniz yok", await HtmlAsync(response));
            Assert.Equal(before, host.Gateway.Requests.Count);
        }
    }

    public static TheoryData<string> PostPages() => new()
    {
        "/Admin/PharmacyMedicines/Create",
        $"/Admin/PharmacyMedicines/Edit/{PharmacyUiTestData.MedicineId}",
        $"/Admin/PharmacyMedicines/Delete/{PharmacyUiTestData.MedicineId}",
        $"/Admin/PharmacyMedicines/Stock/{PharmacyUiTestData.MedicineId}",
        "/Admin/PharmacyDispensing/Dispense",
    };

    // Yazma uçları antiforgery ister: belirteçsiz POST 400, servise hiçbir yazma gitmez.
    [Theory]
    [MemberData(nameof(PostPages))]
    public async Task Every_post_requires_antiforgery_token(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["Name"] = "x" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method is "POST" or "PUT" or "DELETE" or "PATCH" && r.Path != "/gateway/auth/login");
    }

    [Theory]
    [MemberData(nameof(PostPages))]
    public async Task Patient_cannot_post(string path)
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await ClientAsync(host, "Patient");

        var response = await PostAsync(host, client, "/", path, new() { ["prescriptionId"] = PharmacyUiTestData.RxId.ToString() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method is "POST" or "PUT" or "DELETE" && r.Path != "/gateway/auth/login");
    }

    // GET ile silme / karşılama yapılamaz: GET yalnız onay/önizleme gösterir.
    [Fact]
    public async Task Get_pages_never_write()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/medicines/{PharmacyUiTestData.MedicineId}", 200, PharmacyUiTestData.Medicine());
        host.Gateway.MapJson("GET", $"/gateway/pharmacy/dispense/preview/{PharmacyUiTestData.RxId}", 200,
            PharmacyUiTestData.Preview(true, false, PharmacyUiTestData.Line(1, "parol", "Matched", "Parol")));
        var client = await AdminClientAsync(host);

        foreach (var path in new[]
        {
            $"/Admin/PharmacyMedicines/Delete/{PharmacyUiTestData.MedicineId}",
            $"/Admin/PharmacyMedicines/Stock/{PharmacyUiTestData.MedicineId}",
            $"/Admin/PharmacyDispensing?prescriptionId={PharmacyUiTestData.RxId}",
        })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);

        Assert.DoesNotContain(host.Gateway.Requests, r => r.Method != "GET" && r.Path != "/gateway/auth/login");
    }
}

public class PharmacyMedicinesUiTests
{
    private static readonly string Base = "/Admin/PharmacyMedicines";
    private static readonly Guid Id = PharmacyUiTestData.MedicineId;

    [Fact]
    public async Task Index_lists_medicines_with_low_stock_badge_and_action_links()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/medicines", 200, Paged(
        [
            PharmacyUiTestData.Medicine(name: "Parol", stock: 40),
            PharmacyUiTestData.Medicine(PharmacyUiTestData.OtherMedicineId, "Majezik Şurup", "Syrup", stock: 2, minStock: 5, low: true),
        ]));
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync(Base);
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Parol", html);
        Assert.Contains("Majezik Şurup", html);
        Assert.Contains("Şurup", html); // form Türkçe etiketi
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "Düşük stok")); // yalnız düşük stoklu satırda rozet
        Assert.Contains($"{Base}/Edit/{Id}", html);
        Assert.Contains($"{Base}/Stock/{Id}", html);
        Assert.Contains($"{Base}/Movements/{Id}", html);
        Assert.Contains($"{Base}/Delete/{Id}", html);
        Assert.Contains("25.50 TRY", html);
    }

    [Fact]
    public async Task Index_passes_search_and_valid_form_filter_to_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var query = MapCapturingQuery(host, "GET", "/gateway/medicines", 200, Paged([]));
        var client = await AdminClientAsync(host);

        await client.GetAsync($"{Base}?Search=agri&Form=Syrup&page=2");

        Assert.Contains("search=agri", query());
        Assert.Contains("form=Syrup", query());
        Assert.Contains("page=2", query());

        // Geçersiz form süzgeci gateway'e hiç gitmez.
        await client.GetAsync($"{Base}?Form=Macun");
        Assert.DoesNotContain("Macun", query());
    }

    [Fact]
    public async Task Index_empty_state_and_service_down_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/medicines", 200, Paged([]));
        var client = await AdminClientAsync(host);
        Assert.Contains("İlaç bulunamadı", await HtmlAsync(await client.GetAsync(Base)));

        host.Gateway.MapStatus("GET", "/gateway/medicines", 500);
        var down = await client.GetAsync(Base);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.Contains("Servis şu an yanıt vermiyor", await HtmlAsync(down));
    }

    [Fact]
    public async Task Create_posts_parsed_body_and_redirects()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/medicines", 201, PharmacyUiTestData.Medicine());
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, $"{Base}/Create", PharmacyUiTestData.ValidForm());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Base, Location(response));
        var body = Json(Last(host, "POST", "/gateway/medicines"));
        Assert.Equal("Yeni İlaç", body.GetProperty("name").GetString());
        Assert.Equal("Syrup", body.GetProperty("form").GetString());
        Assert.Equal(25.5m, body.GetProperty("price").GetDecimal()); // "25,50" -> 25.5
        Assert.Equal(12, body.GetProperty("stock").GetInt32());
        Assert.Equal(3, body.GetProperty("minStock").GetInt32());
    }

    [Fact]
    public async Task Create_with_missing_fields_or_bad_price_shows_errors_and_calls_nothing()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);
        var form = PharmacyUiTestData.ValidForm();
        form["Name"] = "";
        form["Price"] = "abc";

        var response = await host.PostFormAsync(client, $"{Base}/Create", form);
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("İlaç adı gerekli", html);
        Assert.False(Called(host, "POST", "/gateway/medicines"));

        var badPrice = PharmacyUiTestData.ValidForm();
        badPrice["Price"] = "abc";
        var html2 = await HtmlAsync(await host.PostFormAsync(client, $"{Base}/Create", badPrice));
        Assert.Contains("Fiyat geçerli bir sayı olmalı", html2);
        Assert.False(Called(host, "POST", "/gateway/medicines"));
    }

    [Fact]
    public async Task Create_duplicate_barcode_409_shows_api_message_in_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.MapProblem("POST", "/gateway/medicines", 409, "Bu barkodla kayıtlı bir ilaç zaten var.");
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, $"{Base}/Create", PharmacyUiTestData.ValidForm());
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Bu barkodla kayıtlı bir ilaç zaten var.", html);
        Assert.Contains("Yeni İlaç", html); // girilen değerler kaybolmadı
    }

    [Fact]
    public async Task Create_api_validation_400_maps_errors_to_fields()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/medicines", 400, new
        {
            status = 400,
            title = "Geçersiz istek",
            errors = new Dictionary<string, string[]> { ["Barcode"] = ["Barkod yalnız harf, rakam ve '-' içerebilir."] },
        });
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await host.PostFormAsync(client, $"{Base}/Create", PharmacyUiTestData.ValidForm()));

        Assert.Contains("Barkod yalnız harf, rakam", html);
    }

    [Fact]
    public async Task Edit_shows_current_values_and_sends_rowversion_without_stock()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/medicines/{Id}", 200, PharmacyUiTestData.Medicine());
        host.Gateway.MapJson("PUT", $"/gateway/medicines/{Id}", 200, PharmacyUiTestData.Medicine());
        var client = await AdminClientAsync(host);

        var page = await HtmlAsync(await client.GetAsync($"{Base}/Edit/{Id}"));
        Assert.Contains("Parol", page);
        Assert.Contains("25.50", page);
        Assert.Contains("AAAAAAAAB9E=", page); // gizli RowVersion

        var form = PharmacyUiTestData.ValidForm();
        form["RowVersion"] = "AAAAAAAAB9E=";
        var response = await host.PostFormAsync(client, $"{Base}/Edit/{Id}", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Base, Location(response));
        var body = Json(Last(host, "PUT", $"/gateway/medicines/{Id}"));
        Assert.Equal("AAAAAAAAB9E=", body.GetProperty("rowVersion").GetString());
        Assert.True(!body.TryGetProperty("stock", out var stock) || stock.ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Edit_conflict_409_is_shown_in_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/medicines/{Id}", 200, PharmacyUiTestData.Medicine());
        host.MapProblem("PUT", $"/gateway/medicines/{Id}", 409, "İlaç kaydı siz düzenlerken başka biri tarafından değiştirildi.");
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, $"{Base}/Edit/{Id}", PharmacyUiTestData.ValidForm());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("başka biri tarafından değiştirildi", await HtmlAsync(response));
    }

    [Fact]
    public async Task Edit_unknown_medicine_shows_404_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync($"{Base}/Edit/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_confirms_first_then_deletes_on_post()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/medicines/{Id}", 200, PharmacyUiTestData.Medicine());
        host.Gateway.MapStatus("DELETE", $"/gateway/medicines/{Id}", 204);
        var client = await AdminClientAsync(host);

        var confirm = await HtmlAsync(await client.GetAsync($"{Base}/Delete/{Id}"));
        Assert.Contains("Evet, sil", confirm);
        Assert.False(Called(host, "DELETE", $"/gateway/medicines/{Id}"));

        var response = await host.PostFormAsync(client, $"{Base}/Delete/{Id}", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Base, Location(response));
        Assert.True(Called(host, "DELETE", $"/gateway/medicines/{Id}"));
    }

    [Fact]
    public async Task Stock_adjust_posts_delta_and_reason_then_shows_movements()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/medicines/{Id}", 200, PharmacyUiTestData.Medicine());
        host.Gateway.MapJson("POST", $"/gateway/medicines/{Id}/stock", 200, PharmacyUiTestData.Movement());
        var client = await AdminClientAsync(host);

        var page = await HtmlAsync(await client.GetAsync($"{Base}/Stock/{Id}"));
        Assert.Contains("mevcut stok", page);
        Assert.Contains("40", page);

        var response = await host.PostFormAsync(client, $"{Base}/Stock/{Id}", new() { ["Delta"] = "-2", ["Reason"] = "fire" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{Base}/Movements/{Id}", Location(response));
        var body = Json(Last(host, "POST", $"/gateway/medicines/{Id}/stock"));
        Assert.Equal(-2, body.GetProperty("delta").GetInt32());
        Assert.Equal("fire", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Stock_below_zero_409_message_is_shown_and_form_keeps_context()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/medicines/{Id}", 200, PharmacyUiTestData.Medicine());
        host.MapProblem("POST", $"/gateway/medicines/{Id}/stock", 409, "Stok eksiye düşemez: mevcut stok bu çıkış için yetersiz.");
        var client = await AdminClientAsync(host);

        var response = await host.PostFormAsync(client, $"{Base}/Stock/{Id}", new() { ["Delta"] = "-500", ["Reason"] = "çok" });
        var html = await HtmlAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Stok eksiye düşemez", html);
        Assert.Contains("Parol", html); // ilaç adı yeniden okunup gösterildi
    }

    [Fact]
    public async Task Stock_zero_delta_or_missing_reason_is_rejected_before_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/medicines/{Id}", 200, PharmacyUiTestData.Medicine());
        var client = await AdminClientAsync(host);

        var zero = await HtmlAsync(await host.PostFormAsync(client, $"{Base}/Stock/{Id}", new() { ["Delta"] = "0", ["Reason"] = "x" }));
        var noReason = await HtmlAsync(await host.PostFormAsync(client, $"{Base}/Stock/{Id}", new() { ["Delta"] = "3", ["Reason"] = "" }));

        Assert.Contains("Değişim 0 olamaz", zero);
        Assert.Contains("Neden gerekli", noReason);
        Assert.False(Called(host, "POST", $"/gateway/medicines/{Id}/stock"));
    }

    [Fact]
    public async Task Movements_lists_ledger_with_type_labels_and_prescription_link()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", $"/gateway/medicines/{Id}", 200, PharmacyUiTestData.Medicine());
        host.Gateway.MapJson("GET", $"/gateway/medicines/{Id}/movements", 200, Paged(
        [
            PharmacyUiTestData.Movement("Dispensation", -1, 39, "Reçete karşılama", PharmacyUiTestData.RxId),
            PharmacyUiTestData.Movement("Initial", 40, 40, "İlk stok kaydı"),
        ]));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync($"{Base}/Movements/{Id}"));

        Assert.Contains("Reçete karşılama", html);
        Assert.Contains("İlk stok", html);
        Assert.Contains("-1", html);
        Assert.Contains("+40", html);
        Assert.Contains($"/Admin/PharmacyDispensing?prescriptionId={PharmacyUiTestData.RxId}", html);
    }
}

public class PharmacyDispensingUiTests
{
    private const string Base = "/Admin/PharmacyDispensing";
    private static readonly Guid Rx = PharmacyUiTestData.RxId;

    private static string PreviewPath => $"/gateway/pharmacy/dispense/preview/{Rx}";

    [Fact]
    public async Task Index_without_id_shows_only_the_lookup_form()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync(Base));

        Assert.Contains("Reçete Id", html);
        Assert.DoesNotContain("Karşıla</button>", html);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/pharmacy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Index_with_malformed_id_shows_error_without_calling_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync($"{Base}?prescriptionId=abc"));

        Assert.Contains("geçerli bir kimlik", html);
        Assert.DoesNotContain(host.Gateway.Requests, r => r.Path.StartsWith("/gateway/pharmacy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_of_dispensable_prescription_shows_lines_and_the_dispense_button()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", PreviewPath, 200, PharmacyUiTestData.Preview(true, false,
            PharmacyUiTestData.Line(1, "parol", "Matched", "Parol", 1, 40),
            PharmacyUiTestData.Line(2, "ASPİRİN", "Matched", "Aspirin", 1, 7)));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync($"{Base}?prescriptionId={Rx}"));

        Assert.Contains("parol", html);
        Assert.Contains("Parol", html);
        Assert.Contains("Aspirin", html);
        Assert.Contains("Karşılanabilir", html);
        Assert.Contains("Karşıla</button>", html);
        Assert.Contains("confirm(", html); // onaylı gönderim
        Assert.Contains($"value=\"{Rx}\"", html);
    }

    [Fact]
    public async Task Preview_with_problem_lines_shows_reasons_and_hides_the_dispense_button()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", PreviewPath, 200, PharmacyUiTestData.Preview(false, false,
            PharmacyUiTestData.Line(1, "parol", "Matched", "Parol"),
            PharmacyUiTestData.Line(2, "Bilinmeyen Şurup", "NotFound"),
            PharmacyUiTestData.Line(3, "aspirin", "InsufficientStock", "Aspirin", 1, 0)));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync($"{Base}?prescriptionId={Rx}"));

        Assert.Contains("Katalogda yok", html);
        Assert.Contains("Stok yetersiz", html);
        Assert.Contains("Karşılanamaz", html);
        Assert.DoesNotContain("Karşıla</button>", html);
    }

    [Fact]
    public async Task Preview_of_already_dispensed_prescription_says_so_and_offers_no_button()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", PreviewPath, 200, PharmacyUiTestData.Preview(false, true,
            PharmacyUiTestData.Line(1, "parol", "Matched", "Parol")));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync($"{Base}?prescriptionId={Rx}"));

        Assert.Contains("Zaten karşılandı", html);
        Assert.DoesNotContain("Karşıla</button>", html);
    }

    [Fact]
    public async Task Preview_unknown_prescription_404_is_a_form_message_not_an_error_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", PreviewPath, 404);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync($"{Base}?prescriptionId={Rx}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("reçete bulunamadı", await HtmlAsync(response));
    }

    [Fact]
    public async Task Preview_when_prescription_service_is_down_shows_a_warning_not_an_error_page()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("GET", PreviewPath, 503);
        var client = await AdminClientAsync(host);

        var response = await client.GetAsync($"{Base}?prescriptionId={Rx}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await HtmlAsync(response);
        Assert.Contains("ulaşılamıyor", html);
        Assert.DoesNotContain("127.0.0.1", html);
    }

    [Fact]
    public async Task Dispense_success_posts_the_prescription_id_and_redirects_to_the_list()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("POST", "/gateway/pharmacy/dispense", 201, PharmacyUiTestData.Dispensation());
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/", $"{Base}/Dispense", new() { ["prescriptionId"] = Rx.ToString() });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{Base}/Dispensations", Location(response));
        var body = Json(Last(host, "POST", "/gateway/pharmacy/dispense"));
        Assert.Equal(Rx, body.GetProperty("prescriptionId").GetGuid());
    }

    [Fact]
    public async Task Dispense_conflict_409_goes_back_to_the_preview_with_the_api_message()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.MapProblem("POST", "/gateway/pharmacy/dispense", 409, "Reçete karşılanamadı: bazı kalemler katalogda yok ya da stok yetersiz. Hiçbir ilaç düşülmedi.");
        host.Gateway.MapJson("GET", PreviewPath, 200, PharmacyUiTestData.Preview(false, false,
            PharmacyUiTestData.Line(1, "Yok İlaç", "NotFound")));
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/", $"{Base}/Dispense", new() { ["prescriptionId"] = Rx.ToString() });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith($"{Base}?prescriptionId={Rx}", Location(response));
        var html = await HtmlAsync(await client.GetAsync(Location(response)));
        Assert.Contains("Hiçbir ilaç düşülmedi", html);   // 409 mesajı (flash)
        Assert.Contains("Katalogda yok", html);            // kalem nedeni (önizleme)
        Assert.DoesNotContain("Karşıla</button>", html);
    }

    [Fact]
    public async Task Dispense_already_dispensed_409_message_is_shown()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.MapProblem("POST", "/gateway/pharmacy/dispense", 409, "Bu reçete zaten karşılandı.");
        host.Gateway.MapJson("GET", PreviewPath, 200, PharmacyUiTestData.Preview(false, true, PharmacyUiTestData.Line(1, "parol", "Matched", "Parol")));
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/", $"{Base}/Dispense", new() { ["prescriptionId"] = Rx.ToString() });
        var html = await HtmlAsync(await client.GetAsync(Location(response)));

        Assert.Contains("Bu reçete zaten karşılandı.", html);
    }

    [Fact]
    public async Task Dispense_when_prescription_service_is_down_shows_warning_and_stays_on_the_screen()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapStatus("POST", "/gateway/pharmacy/dispense", 503);
        host.Gateway.MapJson("GET", PreviewPath, 200, PharmacyUiTestData.Preview(true, false, PharmacyUiTestData.Line(1, "parol", "Matched", "Parol")));
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/", $"{Base}/Dispense", new() { ["prescriptionId"] = Rx.ToString() });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("stoğa dokunulmadı", await HtmlAsync(await client.GetAsync(Location(response))));
    }

    [Fact]
    public async Task Dispense_with_malformed_id_does_not_call_the_gateway()
    {
        await using var host = await WebUiTestHost.StartAsync();
        var client = await AdminClientAsync(host);

        var response = await PostAsync(host, client, "/", $"{Base}/Dispense", new() { ["prescriptionId"] = "abc" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(Called(host, "POST", "/gateway/pharmacy/dispense"));
    }

    [Fact]
    public async Task Dispensations_list_shows_medicines_total_and_links_back_to_the_prescription()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/pharmacy/dispensations", 200, Paged([PharmacyUiTestData.Dispensation()]));
        var client = await AdminClientAsync(host);

        var html = await HtmlAsync(await client.GetAsync($"{Base}/Dispensations"));

        Assert.Contains("Parol, Aspirin", html);
        Assert.Contains("51.00 TRY", html);
        Assert.Contains($"{Base}?prescriptionId={Rx}", html);
        Assert.Contains("04.10.2026 13:00", html); // 10:00 UTC -> İstanbul (UTC+3)
    }

    [Fact]
    public async Task Dispensations_list_empty_state()
    {
        await using var host = await WebUiTestHost.StartAsync();
        host.Gateway.MapJson("GET", "/gateway/pharmacy/dispensations", 200, Paged([]));
        var client = await AdminClientAsync(host);

        Assert.Contains("Henüz karşılanan reçete yok", await HtmlAsync(await client.GetAsync($"{Base}/Dispensations")));
    }
}
