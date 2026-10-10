using System.Net;
using System.Net.Http.Json;
using Hastane.Market.Api.Dtos;

namespace Hastane.Market.Tests;

// Ürün CRUD, doğrulama ve yetki. Her test benzersiz adlı ürünlerle çalışır (liste testleri benzersiz arama sözcüğüyle).
[Collection(MarketCollection.Name)]
public class ProductCrudTests(MarketApiFactory factory) : MarketTestBase(factory)
{
    [Fact]
    public async Task Anonymous_CanGetProductById_AndSeesAllContractFields()
    {
        var created = await CreateProductAsync(price: 249.90m, stock: 7, category: "MedicalDevice");

        var response = await Anonymous.GetAsync($"/api/products/{created.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonBodyAsync(response);
        foreach (var field in new[] { "id", "name", "description", "category", "price", "currency", "stock", "imageUrl", "isActive" })
            Assert.True(json.TryGetProperty(field, out _), $"'{field}' alanı yok.");
        Assert.Equal("MedicalDevice", json.GetProperty("category").GetString());
        Assert.Equal("TRY", json.GetProperty("currency").GetString());
        Assert.Equal(249.90m, json.GetProperty("price").GetDecimal());
        Assert.Equal(7, json.GetProperty("stock").GetInt32());
    }

    [Fact]
    public async Task Create_AsAdmin_Returns201_WithLocationHeader()
    {
        var response = await Admin.PostAsJsonAsync("/api/products", ProductBody(name: "  Boşluklu Ad  "), Json, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsync<ResultProductDto>(response);
        Assert.Equal("Boşluklu Ad", created.Name); // kenar boşlukları kırpılır
        Assert.Equal("TRY", created.Currency);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.EndsWith($"/api/products/{created.Id}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Update_ReplacesAllFields_AndPersists()
    {
        var created = await CreateProductAsync(stock: 5);
        var newName = Unique("Yeni");

        var response = await Admin.PutAsJsonAsync($"/api/products/{created.Id}",
            ProductBody(name: newName, price: 55.5m, stock: 42, isActive: false, category: "Care", description: "Yeni açıklama", imageUrl: null), Json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var truth = await GetTruthAsync(created.Id);
        Assert.Equal(newName, truth.Name);
        Assert.Equal(55.5m, truth.Price);
        Assert.Equal(42, truth.Stock);
        Assert.False(truth.IsActive);
        Assert.Equal("Care", truth.Category);
        Assert.Null(truth.ImageUrl);
    }

    [Fact]
    public async Task Update_UnknownProduct_Returns404()
    {
        var response = await Admin.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", ProductBody(), Json, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesProduct_ThenReturns404()
    {
        var created = await CreateProductAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await Admin.DeleteAsync($"/api/products/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous.GetAsync($"/api/products/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.DeleteAsync($"/api/products/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task GetById_NonGuidPath_Returns404Not500()
        => Assert.Equal(HttpStatusCode.NotFound, (await Anonymous.GetAsync("/api/products/abc", Ct)).StatusCode);

    // --- Doğrulama ---

    // Ham JSON: anonim nesneler xunit teori verisi olarak serileştirilemez; metin olarak veririz.
    public static TheoryData<string, string> InvalidBodies() => new()
    {
        { "kategori geçersiz", """{"name":"Ürün X","category":"Foo","price":10,"stock":1}""" },
        { "kategori sayı", """{"name":"Ürün X","category":1,"price":10,"stock":1}""" },
        { "kategori yok", """{"name":"Ürün X","price":10,"stock":1}""" },
        { "ad kısa", """{"name":"A","category":"Vitamin","price":10,"stock":1}""" },
        { "ad yok", """{"category":"Vitamin","price":10,"stock":1}""" },
        { "fiyat sıfır", """{"name":"Ürün X","category":"Vitamin","price":0,"stock":1}""" },
        { "fiyat negatif", """{"name":"Ürün X","category":"Vitamin","price":-5,"stock":1}""" },
        { "fiyat 3 ondalık", """{"name":"Ürün X","category":"Vitamin","price":10.123,"stock":1}""" },
        { "stok negatif", """{"name":"Ürün X","category":"Vitamin","price":10,"stock":-1}""" },
        { "para birimi USD", """{"name":"Ürün X","category":"Vitamin","price":10,"stock":1,"currency":"USD"}""" },
        { "görsel javascript", """{"name":"Ürün X","category":"Vitamin","price":10,"stock":1,"imageUrl":"javascript:alert(1)"}""" },
        { "görsel protokolsüz", """{"name":"Ürün X","category":"Vitamin","price":10,"stock":1,"imageUrl":"//evil.example/x.png"}""" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_InvalidBody_Returns400(string caseName, string json)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await Admin.PostAsync("/api/products", content, Ct);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{caseName}: {response.StatusCode}");
    }
    // --- Yetki ---

    [Fact]
    public async Task Write_Endpoints_WithoutToken_Return401()
    {
        var id = (await CreateProductAsync()).Id;
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.PostAsJsonAsync("/api/products", ProductBody(), Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.PutAsJsonAsync($"/api/products/{id}", ProductBody(), Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous.DeleteAsync($"/api/products/{id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Write_Endpoints_AsPatientOrDoctor_Return403_AndDoNotChangeData()
    {
        var product = await CreateProductAsync(name: Unique("Korumalı"));
        foreach (var client in new[] { PatientClient(Unique("pat")), ClientFor(DoctorToken()) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/products", ProductBody(), Json, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/products/{product.Id}", ProductBody(name: "Hacklendi"), Json, Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/products/{product.Id}", Ct)).StatusCode);
        }

        Assert.Equal(product.Name, (await GetTruthAsync(product.Id)).Name);
    }

    // --- Pasif ürün ---

    [Fact]
    public async Task InactiveProduct_IsHiddenFromEveryoneButAdmin()
    {
        var token = Unique("pasif");
        var inactive = await CreateProductAsync(name: $"{token} Pasif", isActive: false);
        await CreateProductAsync(name: $"{token} Aktif");
        var patient = PatientClient(Unique("pat"));

        // Anonim ve hasta: yalnız aktif; ?includeInactive=true bile pasifi açmaz.
        foreach (var client in new[] { Anonymous, patient })
        {
            var page = await ReadAsync<PagedResult<ResultProductDto>>(await client.GetAsync($"/api/products?search={token}&includeInactive=true", Ct));
            Assert.Equal(1, page.TotalCount);
            Assert.DoesNotContain(page.Items, p => p.Id == inactive.Id);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/products/{inactive.Id}", Ct)).StatusCode);
        }

        // Admin: istenirse hepsi.
        var adminAll = await ReadAsync<PagedResult<ResultProductDto>>(await Admin.GetAsync($"/api/products?search={token}&includeInactive=true", Ct));
        Assert.Equal(2, adminAll.TotalCount);
        var adminDefault = await ReadAsync<PagedResult<ResultProductDto>>(await Admin.GetAsync($"/api/products?search={token}", Ct));
        Assert.Equal(1, adminDefault.TotalCount);
        Assert.Equal(HttpStatusCode.OK, (await Admin.GetAsync($"/api/products/{inactive.Id}", Ct)).StatusCode);
    }

    // --- Liste: süzgeç, arama, sayfalama ---

    [Fact]
    public async Task List_PagesSearchAndCategoryFilter_Work()
    {
        var token = Unique("liste");
        await CreateProductAsync(name: $"{token} A", category: "Vitamin");
        await CreateProductAsync(name: $"{token} B", category: "Vitamin");
        await CreateProductAsync(name: $"{token} C", category: "FirstAid");

        var all = await ReadAsync<PagedResult<ResultProductDto>>(await Anonymous.GetAsync($"/api/products?search={token}&pageSize=2&page=1", Ct));
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal(2, all.PageSize);

        var second = await ReadAsync<PagedResult<ResultProductDto>>(await Anonymous.GetAsync($"/api/products?search={token}&pageSize=2&page=2", Ct));
        Assert.Single(second.Items);
        Assert.Equal(2, second.Page);

        var vitamins = await ReadAsync<PagedResult<ResultProductDto>>(await Anonymous.GetAsync($"/api/products?search={token}&category=vitamin", Ct));
        Assert.Equal(2, vitamins.TotalCount);
        Assert.All(vitamins.Items, p => Assert.Equal("Vitamin", p.Category));

        var none = await ReadAsync<PagedResult<ResultProductDto>>(await Anonymous.GetAsync($"/api/products?search={token}-yok", Ct));
        Assert.Equal(0, none.TotalCount);
    }

    [Fact]
    public async Task List_SearchWildcards_AreLiteral()
    {
        var token = Unique("joker");
        await CreateProductAsync(name: $"{token} normal");

        // % kullanıcı girdisidir, joker değil: hiçbir ürün adı '%' içermiyor.
        var page = await ReadAsync<PagedResult<ResultProductDto>>(await Anonymous.GetAsync("/api/products?search=%25&pageSize=50", Ct));
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData("category=Foo")]
    [InlineData("category=1")]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_InvalidQuery_Returns400(string query)
        => Assert.Equal(HttpStatusCode.BadRequest, (await Anonymous.GetAsync($"/api/products?{query}", Ct)).StatusCode);
}
