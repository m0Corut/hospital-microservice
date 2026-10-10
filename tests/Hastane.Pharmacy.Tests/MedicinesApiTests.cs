using System.Net;
using System.Net.Http.Json;
using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Entities;
using Hastane.Pharmacy.Api.Features.Medicines;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Tests;

[Collection(PharmacyCollection.Name)]
public class MedicinesApiTests : PharmacyTestBase
{
    public MedicinesApiTests(PharmacyApiFactory factory) : base(factory)
    {
    }

    // --- Oluşturma ---

    [Fact]
    public async Task Create_Returns201_WithLocation_GuidV7_AndDefaults()
    {
        var body = NewMedicineBody(name: UniqueName("Aspirin"), stock: 40, minStock: 10, price: 12.75m);

        var response = await Client.PostAsJsonAsync("/api/medicines", body, Json, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await ReadAsync<MedicineDto>(response);
        Assert.Equal(7, dto.Id.Version);
        Assert.EndsWith($"/api/medicines/{dto.Id}", response.Headers.Location!.ToString());
        Assert.Equal(MedicineForm.Tablet, dto.Form);
        Assert.Equal(12.75m, dto.Price);
        Assert.Equal("TRY", dto.Currency);
        Assert.Equal(40, dto.Stock);
        Assert.True(dto.IsActive);
        Assert.False(dto.IsLowStock);
        Assert.NotEmpty(dto.RowVersion);
    }

    [Fact]
    public async Task Create_DuplicateBarcode_Returns409()
    {
        var barcode = NewBarcode();
        await CreateMedicineAsync(barcode: barcode);

        var response = await Client.PostAsJsonAsync("/api/medicines", NewMedicineBody(barcode: barcode), Json, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentSameBarcode_OnlyOneSucceeds()
    {
        var barcode = NewBarcode();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Client.PostAsJsonAsync("/api/medicines", NewMedicineBody(barcode: barcode), Json, Ct)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Create_InvalidBody_Returns400_WithFieldErrors_AndSavesNothing()
    {
        var barcode = NewBarcode();
        var body = NewMedicineBody(name: "", stock: -1, barcode: barcode, price: 1.234m);

        var response = await Client.PostAsJsonAsync("/api/medicines", body, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("Stock", out _));
        Assert.True(errors.TryGetProperty("Price", out _));
        Assert.False(await WithDbAsync(db => db.Medicines.AnyAsync(m => m.Barcode == barcode, Ct)));
    }

    [Fact]
    public async Task Create_UnknownForm_Returns400()
    {
        var response = await Client.PostAsJsonAsync("/api/medicines", NewMedicineBody(form: "Macun"), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithStock_WritesInitialMovement()
    {
        var medicine = await CreateMedicineAsync(stock: 7);

        var movements = await WithDbAsync(db => db.StockMovements.Where(s => s.MedicineId == medicine.Id).ToListAsync(Ct));

        var initial = Assert.Single(movements);
        Assert.Equal(StockMovementType.Initial, initial.Type);
        Assert.Equal(7, initial.Delta);
        Assert.Equal(7, initial.StockAfter);
    }

    [Fact]
    public async Task Create_WithZeroStock_WritesNoMovement()
    {
        var medicine = await CreateMedicineAsync(stock: 0);
        Assert.Empty(await WithDbAsync(db => db.StockMovements.Where(s => s.MedicineId == medicine.Id).ToListAsync(Ct)));
    }

    // --- Okuma ---

    [Fact]
    public async Task GetById_ReturnsMedicine_AndUnknownIdReturns404()
    {
        var medicine = await CreateMedicineAsync();

        Assert.Equal(medicine.Name, (await GetMedicineAsync(medicine.Id)).Name);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/medicines/{Guid.NewGuid()}", Ct)).StatusCode);
        // Guid olmayan yol hiçbir rotayla eşleşmez: 500 değil 404.
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/medicines/abc", Ct)).StatusCode);
    }

    [Fact]
    public async Task List_Search_IgnoresCaseAndTurkishLetters()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var medicine = await CreateMedicineAsync(name: $"Ağrı Kesici {token}");

        foreach (var term in new[] { $"agri kesici {token}", $"AĞRI KESİCİ {token}", $"AGRI KESICI {token}", token })
        {
            var page = await ReadAsync<PagedResult<MedicineDto>>(await Client.GetAsync($"/api/medicines?search={Uri.EscapeDataString(term)}", Ct));
            Assert.Contains(page.Items, m => m.Id == medicine.Id);
        }
    }

    [Fact]
    public async Task List_Search_MatchesActiveIngredientAndExactBarcode()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var barcode = NewBarcode();
        var body = NewMedicineBody(ingredient: $"Etken{token}", barcode: barcode);
        var created = await ReadAsync<MedicineDto>(await Client.PostAsJsonAsync("/api/medicines", body, Json, Ct));

        var byIngredient = await ReadAsync<PagedResult<MedicineDto>>(await Client.GetAsync($"/api/medicines?search=ETKEN{token}", Ct));
        var byBarcode = await ReadAsync<PagedResult<MedicineDto>>(await Client.GetAsync($"/api/medicines?search={barcode}", Ct));

        Assert.Contains(byIngredient.Items, m => m.Id == created.Id);
        Assert.Contains(byBarcode.Items, m => m.Id == created.Id);
    }

    [Fact]
    public async Task List_FormFilter_PagingAndTotalCount()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        await CreateMedicineAsync(name: $"Krem {token} A", form: "Cream");
        await CreateMedicineAsync(name: $"Krem {token} B", form: "Cream");
        await CreateMedicineAsync(name: $"Krem {token} C", form: "Cream");
        await CreateMedicineAsync(name: $"Krem {token} D", form: "Tablet");

        var first = await ReadAsync<PagedResult<MedicineDto>>(await Client.GetAsync($"/api/medicines?search=krem {token}&form=Cream&page=1&pageSize=2", Ct));
        var second = await ReadAsync<PagedResult<MedicineDto>>(await Client.GetAsync($"/api/medicines?search=krem {token}&form=Cream&page=2&pageSize=2", Ct));

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.All(first.Items.Concat(second.Items), m => Assert.Equal(MedicineForm.Cream, m.Form));
        Assert.Equal(3, first.Items.Concat(second.Items).Select(m => m.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("form=Macun")]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_BadQuery_Returns400(string query)
        => Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/medicines?" + query, Ct)).StatusCode);

    // --- Güncelleme ---

    [Fact]
    public async Task Update_ChangesFields_ButNeverTheStock()
    {
        var medicine = await CreateMedicineAsync(stock: 12, minStock: 3);
        var update = new
        {
            name = medicine.Name + " Forte",
            activeIngredient = "Yeni Etken",
            form = "Capsule",
            strength = "1000 mg",
            barcode = medicine.Barcode,
            price = 99.90m,
            minStock = 20,
            requiresPrescription = true,
            stock = 9999, // gönderilse de yok sayılır
        };

        var response = await Client.PutAsJsonAsync($"/api/medicines/{medicine.Id}", update, Json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await ReadAsync<MedicineDto>(response);
        Assert.Equal(MedicineForm.Capsule, dto.Form);
        Assert.Equal(99.90m, dto.Price);
        Assert.Equal(20, dto.MinStock);
        Assert.True(dto.RequiresPrescription);
        Assert.Equal(12, dto.Stock);
        Assert.True(dto.IsLowStock); // 12 <= 20
    }

    [Fact]
    public async Task Update_StaleRowVersion_Returns409_AndKeepsFirstChange()
    {
        var medicine = await CreateMedicineAsync();
        object Body(string name, byte[] rowVersion) => new
        {
            name,
            activeIngredient = medicine.ActiveIngredient,
            form = "Tablet",
            strength = medicine.Strength,
            barcode = medicine.Barcode,
            price = medicine.Price,
            minStock = medicine.MinStock,
            requiresPrescription = false,
            rowVersion,
        };

        // İki yönetici aynı formu (aynı RowVersion ile) açtı.
        var first = await Client.PutAsJsonAsync($"/api/medicines/{medicine.Id}", Body("Birinci", medicine.RowVersion), Json, Ct);
        var second = await Client.PutAsJsonAsync($"/api/medicines/{medicine.Id}", Body("Ikinci", medicine.RowVersion), Json, Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Birinci", (await GetMedicineAsync(medicine.Id)).Name);
    }

    [Fact]
    public async Task Update_AfterStockChange_StaleRowVersionIsRejected_FreshOneAccepted()
    {
        var medicine = await CreateMedicineAsync();
        await AdjustAsync(medicine.Id, +5); // stok hareketi de satırın RowVersion'ını yeniler

        object Body(byte[] rowVersion) => new
        {
            name = medicine.Name,
            activeIngredient = medicine.ActiveIngredient,
            form = "Tablet",
            strength = medicine.Strength,
            barcode = medicine.Barcode,
            price = 30m,
            minStock = medicine.MinStock,
            requiresPrescription = false,
            rowVersion,
        };

        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"/api/medicines/{medicine.Id}", Body(medicine.RowVersion), Json, Ct)).StatusCode);

        var fresh = await GetMedicineAsync(medicine.Id);
        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/medicines/{medicine.Id}", Body(fresh.RowVersion), Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Update_WithoutRowVersion_IsAllowed_AndBarcodeClashReturns409()
    {
        var a = await CreateMedicineAsync();
        var b = await CreateMedicineAsync();

        object Body(string barcode) => new
        {
            name = a.Name,
            activeIngredient = a.ActiveIngredient,
            form = "Tablet",
            strength = a.Strength,
            barcode,
            price = a.Price,
            minStock = a.MinStock,
            requiresPrescription = false,
        };

        Assert.Equal(HttpStatusCode.OK, (await Client.PutAsJsonAsync($"/api/medicines/{a.Id}", Body(a.Barcode), Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PutAsJsonAsync($"/api/medicines/{a.Id}", Body(b.Barcode), Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Update_UnknownId_Returns404_InvalidBodyReturns400()
    {
        var medicine = await CreateMedicineAsync();
        var valid = new { name = "X", activeIngredient = "Y", form = "Tablet", strength = "1 mg", barcode = NewBarcode(), price = 1m, minStock = 0, requiresPrescription = false };

        Assert.Equal(HttpStatusCode.NotFound, (await Client.PutAsJsonAsync($"/api/medicines/{Guid.NewGuid()}", valid, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PutAsJsonAsync($"/api/medicines/{medicine.Id}", valid with { price = -5m }, Json, Ct)).StatusCode);
    }

    // --- Silme (yumuşak) ---

    [Fact]
    public async Task Delete_IsSoft_HidesFromReadsButKeepsHistory()
    {
        var medicine = await CreateMedicineAsync(stock: 4);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/medicines/{medicine.Id}", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/medicines/{medicine.Id}", Ct)).StatusCode);
        var list = await ReadAsync<PagedResult<MedicineDto>>(await Client.GetAsync($"/api/medicines?search={medicine.Barcode}", Ct));
        Assert.DoesNotContain(list.Items, m => m.Id == medicine.Id);
        // Satır duruyor, defter okunabiliyor.
        Assert.False((await WithDbAsync(db => db.Medicines.SingleAsync(m => m.Id == medicine.Id, Ct))).IsActive);
        var movements = await Client.GetAsync($"/api/medicines/{medicine.Id}/movements", Ct);
        Assert.Equal(HttpStatusCode.OK, movements.StatusCode);
        // Tekrar silmek: artık yok.
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/medicines/{medicine.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownId_Returns404()
        => Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/medicines/{Guid.NewGuid()}", Ct)).StatusCode);

    // --- Düşük stok ---

    [Fact]
    public async Task LowStock_ListsOnlyAtOrBelowMinimum_AndFlagsThem()
    {
        var low = await CreateMedicineAsync(stock: 2, minStock: 5);
        var edge = await CreateMedicineAsync(stock: 5, minStock: 5);
        var fine = await CreateMedicineAsync(stock: 6, minStock: 5);
        var inactive = await CreateMedicineAsync(stock: 0, minStock: 5);
        await Client.DeleteAsync($"/api/medicines/{inactive.Id}", Ct);

        var all = new List<MedicineDto>();
        for (var page = 1; ; page++)
        {
            var result = await ReadAsync<PagedResult<MedicineDto>>(await Client.GetAsync($"/api/medicines/low-stock?page={page}&pageSize=100", Ct));
            all.AddRange(result.Items);
            if (all.Count >= result.TotalCount) break;
        }

        Assert.Contains(all, m => m.Id == low.Id && m.IsLowStock);
        Assert.Contains(all, m => m.Id == edge.Id);
        Assert.DoesNotContain(all, m => m.Id == fine.Id);
        Assert.DoesNotContain(all, m => m.Id == inactive.Id);
        Assert.All(all, m => Assert.True(m.Stock <= m.MinStock));
    }

    // --- Hareketler ---

    [Fact]
    public async Task Movements_NewestFirst_PagedAndUnknownMedicineIs404()
    {
        var medicine = await CreateMedicineAsync(stock: 10);
        await AdjustAsync(medicine.Id, -3, "fire");
        await AdjustAsync(medicine.Id, +8, "mal kabul");

        var page = await ReadAsync<PagedResult<StockMovementDto>>(await Client.GetAsync($"/api/medicines/{medicine.Id}/movements?page=1&pageSize=2", Ct));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("mal kabul", page.Items[0].Reason);
        Assert.Equal(15, page.Items[0].StockAfter);
        Assert.Equal(StockMovementType.Adjustment, page.Items[0].Type);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/medicines/{Guid.NewGuid()}/movements", Ct)).StatusCode);
    }

    // --- Loglama: gövde yok ---

    [Fact]
    public async Task Logs_ContainRequestName_ButNotTheBody()
    {
        var secretName = "GizliIlacAdi" + Guid.NewGuid().ToString("N")[..10];
        await CreateMedicineAsync(name: secretName);

        var logs = Factory.Logs.Entries;

        Assert.Contains(logs, l => l.Contains("CreateMedicineCommand", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, l => l.Contains(secretName, StringComparison.Ordinal));
    }
}
