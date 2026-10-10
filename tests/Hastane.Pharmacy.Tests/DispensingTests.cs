using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Entities;
using Hastane.Pharmacy.Api.Features.Dispensing;
using Hastane.Pharmacy.Api.Features.Medicines;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Tests;

[Collection(PharmacyCollection.Name)]
public class DispensingTests : PharmacyTestBase
{
    public DispensingTests(PharmacyApiFactory factory) : base(factory)
    {
    }

    private async Task<int> StockAsync(MedicineDto medicine) => (await GetMedicineAsync(medicine.Id)).Stock;

    private async Task<bool> WasDispensedAsync(Guid prescriptionId)
        => (await Client.GetAsync($"/api/pharmacy/dispensations/by-prescription/{prescriptionId}", Ct)).StatusCode == HttpStatusCode.OK;

    private static JsonElement[] Items(JsonElement problem) => problem.GetProperty("items").EnumerateArray().ToArray();

    // --- Başarılı karşılama ---

    [Fact]
    public async Task Dispense_AllItemsAvailable_DecrementsEveryStock_AndRecordsEverything()
    {
        var a = await CreateMedicineAsync(stock: 5, price: 10m);
        var b = await CreateMedicineAsync(stock: 2, price: 20.50m);
        var prescriptionId = FakePrescriptionService.Register(a.Name, b.Name);

        var response = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dispensation = await ReadAsync<DispensationDto>(response);
        Assert.Equal(prescriptionId, dispensation.PrescriptionId);
        Assert.Equal(2, dispensation.Lines.Count);
        Assert.Equal(30.50m, dispensation.Total);
        Assert.Equal(4, await StockAsync(a));
        Assert.Equal(1, await StockAsync(b));

        // Hareketler: her ilaç için reçeteye bağlı bir "Dispensation" satırı.
        var movements = await WithDbAsync(db => db.StockMovements
            .Where(s => s.PrescriptionId == prescriptionId).OrderBy(s => s.MedicineId).ToListAsync(Ct));
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m =>
        {
            Assert.Equal(StockMovementType.Dispensation, m.Type);
            Assert.Equal(-1, m.Delta);
        });
        Assert.Equal(new[] { 4, 1 }.Order(), movements.Select(m => m.StockAfter).Order());
    }

    [Fact]
    public async Task Dispense_MatchesNamesIgnoringCaseAndTurkishLetters()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var medicine = await CreateMedicineAsync(name: $"Ağrı Kesici {token}", stock: 3);
        var prescriptionId = FakePrescriptionService.Register($"AGRI KESICI {token}");

        var response = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, await StockAsync(medicine));
        var line = Assert.Single((await ReadAsync<DispensationDto>(response)).Lines);
        Assert.Equal($"AGRI KESICI {token}", line.PrescriptionMedicineName); // reçetede yazan
        Assert.Equal(medicine.Name, line.MedicineName);                      // katalogdaki ad
    }

    [Fact]
    public async Task Dispense_StoresPriceSnapshot()
    {
        var medicine = await CreateMedicineAsync(stock: 3, price: 10m);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name);
        await DispenseAsync(prescriptionId);

        // Fiyat sonradan değişir; karşılama kaydı eski fiyatı korur.
        await Client.PutAsJsonAsync($"/api/medicines/{medicine.Id}", new
        {
            name = medicine.Name,
            activeIngredient = medicine.ActiveIngredient,
            form = "Tablet",
            strength = medicine.Strength,
            barcode = medicine.Barcode,
            price = 99m,
            minStock = 1,
            requiresPrescription = false,
        }, Json, Ct);

        var record = await ReadAsync<DispensationDto>(await Client.GetAsync($"/api/pharmacy/dispensations/by-prescription/{prescriptionId}", Ct));
        Assert.Equal(10m, Assert.Single(record.Lines).UnitPrice);
    }

    [Fact]
    public async Task Dispense_ForwardsTheCallersAuthorizationToThePrescriptionService()
    {
        var medicine = await CreateMedicineAsync(stock: 1);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name);

        await DispenseAsync(prescriptionId);

        var call = FakePrescriptionService.Calls.Last(c => c.Path.EndsWith(prescriptionId.ToString("D"), StringComparison.Ordinal));
        Assert.Equal("Bearer " + AdminJwt, call.Authorization);
    }

    [Fact]
    public async Task Dispense_SameMedicineTwiceOnPrescription_DecrementsByTwo()
    {
        var medicine = await CreateMedicineAsync(stock: 3);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name, medicine.Name.ToUpperInvariant());

        var response = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await StockAsync(medicine));
        Assert.Equal(2, (await ReadAsync<DispensationDto>(response)).Lines.Count);
    }

    // --- Hepsi ya hiç ---

    [Fact]
    public async Task Dispense_UnmatchedItem_ChangesNothing_AndListsTheItem()
    {
        var known = await CreateMedicineAsync(stock: 5);
        var prescriptionId = FakePrescriptionService.Register(known.Name, "Katalogda Olmayan İlaç " + Guid.NewGuid().ToString("N")[..6]);

        var response = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var items = Items(await ReadJsonAsync(response));
        var failed = Assert.Single(items);
        Assert.Equal("NotFound", failed.GetProperty("status").GetString());
        Assert.Equal(2, failed.GetProperty("lineNo").GetInt32());
        Assert.Equal(5, await StockAsync(known)); // eşleşen kalemin stoğu da düşmedi
        Assert.False(await WasDispensedAsync(prescriptionId));
        Assert.Empty(await WithDbAsync(db => db.StockMovements.Where(s => s.PrescriptionId == prescriptionId).ToListAsync(Ct)));
    }

    [Fact]
    public async Task Dispense_InsufficientStock_ChangesNothing_AndReportsAvailability()
    {
        var plenty = await CreateMedicineAsync(stock: 9);
        var empty = await CreateMedicineAsync(stock: 0);
        var prescriptionId = FakePrescriptionService.Register(plenty.Name, empty.Name);

        var response = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var failed = Assert.Single(Items(await ReadJsonAsync(response)));
        Assert.Equal("InsufficientStock", failed.GetProperty("status").GetString());
        Assert.Equal(1, failed.GetProperty("required").GetInt32());
        Assert.Equal(0, failed.GetProperty("available").GetInt32());
        Assert.Equal(9, await StockAsync(plenty));
        Assert.Equal(0, await StockAsync(empty));
        Assert.False(await WasDispensedAsync(prescriptionId));
    }

    [Fact]
    public async Task Dispense_MixOfProblems_ReportsAllProblemItems_AndTouchesNone()
    {
        var good = await CreateMedicineAsync(stock: 4);
        var empty = await CreateMedicineAsync(stock: 0);
        var prescriptionId = FakePrescriptionService.Register(good.Name, empty.Name, "Yok " + Guid.NewGuid().ToString("N")[..6]);

        var response = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var statuses = Items(await ReadJsonAsync(response)).Select(i => i.GetProperty("status").GetString()).Order().ToArray();
        Assert.Equal(new[] { "InsufficientStock", "NotFound" }, statuses);
        Assert.Equal(4, await StockAsync(good));
    }

    [Fact]
    public async Task Dispense_SameMedicineTwice_ButStockForOnlyOne_IsInsufficient()
    {
        var medicine = await CreateMedicineAsync(stock: 1);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name, medicine.Name);

        var response = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, Items(await ReadJsonAsync(response)).Length); // iki kalem de işaretli
        Assert.Equal(1, await StockAsync(medicine));
    }

    [Fact]
    public async Task Dispense_DeletedMedicine_CountsAsNotFound()
    {
        var medicine = await CreateMedicineAsync(stock: 5);
        await Client.DeleteAsync($"/api/medicines/{medicine.Id}", Ct);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name);

        var response = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NotFound", Assert.Single(Items(await ReadJsonAsync(response))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Dispense_AmbiguousName_IsRefused()
    {
        // Aynı normalleşmiş ada sahip iki aktif ilaç: hangisini düşeceğimiz belli değil, tahmin etmeyiz.
        var token = Guid.NewGuid().ToString("N")[..8];
        var a = await CreateMedicineAsync(name: $"Ağrı Kes {token}", stock: 5);
        var b = await CreateMedicineAsync(name: $"AGRI KES {token}", stock: 5);
        var prescriptionId = FakePrescriptionService.Register($"agri kes {token}");

        var response = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Ambiguous", Assert.Single(Items(await ReadJsonAsync(response))).GetProperty("status").GetString());
        Assert.Equal(5, await StockAsync(a));
        Assert.Equal(5, await StockAsync(b));
    }

    // --- İkinci kez ---

    [Fact]
    public async Task Dispense_SecondTime_Returns409_StockDecrementedOnlyOnce_NoSecondLookup()
    {
        var medicine = await CreateMedicineAsync(stock: 5);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name);

        var first = await DispenseAsync(prescriptionId);
        var callsAfterFirst = FakePrescriptionService.CallCount(prescriptionId);
        var second = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("zaten karşılandı", (await ReadJsonAsync(second)).GetProperty("detail").GetString());
        Assert.Equal(4, await StockAsync(medicine));
        Assert.Equal(callsAfterFirst, FakePrescriptionService.CallCount(prescriptionId)); // ikinci istek Prescription'a hiç gitmedi
    }

    [Fact]
    public async Task Dispense_SamePrescriptionConcurrently_ExactlyOneWins_StockMovesOnce()
    {
        var medicine = await CreateMedicineAsync(stock: 10);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => DispenseAsync(prescriptionId)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(9, await StockAsync(medicine));
        Assert.Equal(1, await WithDbAsync(db => db.Dispensations.CountAsync(d => d.PrescriptionId == prescriptionId, Ct)));
        Assert.Equal(1, await WithDbAsync(db => db.StockMovements.CountAsync(s => s.PrescriptionId == prescriptionId, Ct)));
    }

    [Fact]
    public async Task Dispense_TwoPrescriptionsRaceForLastUnit_OneWins_StockNeverNegative()
    {
        var medicine = await CreateMedicineAsync(stock: 1);
        var p1 = FakePrescriptionService.Register(medicine.Name);
        var p2 = FakePrescriptionService.Register(medicine.Name);

        var responses = await Task.WhenAll(DispenseAsync(p1), DispenseAsync(p2));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await StockAsync(medicine));
        Assert.Equal(1, await WithDbAsync(db => db.Dispensations.CountAsync(d => d.PrescriptionId == p1 || d.PrescriptionId == p2, Ct)));
    }

    [Fact]
    public async Task Dispense_RaceLostMidway_RollsBackEarlierMedicinesToo()
    {
        // İki reçete aynı iki ilacı ister, ikinci ilacın tek ünitesi var: biri kazanır, kaybeden HİÇBİR ilacı düşmemiş olmalı.
        var abundant = await CreateMedicineAsync(stock: 20);
        var scarce = await CreateMedicineAsync(stock: 1);
        var p1 = FakePrescriptionService.Register(abundant.Name, scarce.Name);
        var p2 = FakePrescriptionService.Register(abundant.Name, scarce.Name);

        var responses = await Task.WhenAll(DispenseAsync(p1), DispenseAsync(p2));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(0, await StockAsync(scarce));
        Assert.Equal(19, await StockAsync(abundant)); // 20 - 1 (yalnız kazanan); kaybeden geri alındı
    }

    // --- Prescription servisi ---

    [Fact]
    public async Task Dispense_UnknownPrescription_Returns404()
    {
        var response = await DispenseAsync(Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(PrescriptionMode.Down)]
    [InlineData(PrescriptionMode.Timeout)]
    [InlineData(PrescriptionMode.Error500)]
    [InlineData(PrescriptionMode.BadJson)]
    public async Task Dispense_PrescriptionServiceUnavailable_Returns503_AndTouchesNoStock(PrescriptionMode mode)
    {
        var medicine = await CreateMedicineAsync(stock: 5);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name);
        FakePrescriptionService.Mode = mode;

        var response = await DispenseAsync(prescriptionId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(5, await StockAsync(medicine));
        Assert.False(await WasDispensedAsync(prescriptionId));
    }

    [Fact]
    public async Task Dispense_PrescriptionServiceForbids_Returns403()
    {
        var medicine = await CreateMedicineAsync(stock: 5);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name);
        FakePrescriptionService.Mode = PrescriptionMode.Forbidden403;

        Assert.Equal(HttpStatusCode.Forbidden, (await DispenseAsync(prescriptionId)).StatusCode);
        Assert.Equal(5, await StockAsync(medicine));
    }

    [Fact]
    public async Task Dispense_EmptyPrescription_Returns409()
    {
        var prescriptionId = FakePrescriptionService.Register();
        Assert.Equal(HttpStatusCode.Conflict, (await DispenseAsync(prescriptionId)).StatusCode);
    }

    [Fact]
    public async Task Dispense_EmptyGuid_Returns400_WithoutCallingPrescriptionService()
    {
        var before = FakePrescriptionService.Calls.Count;

        var response = await DispenseAsync(Guid.Empty);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, FakePrescriptionService.Calls.Count); // geçersiz komut handler'a (ve dış çağrıya) ulaşmadı
    }

    // --- Önizleme ---

    [Fact]
    public async Task Preview_ShowsMatchesAndStock_WithoutChangingAnything()
    {
        var ok = await CreateMedicineAsync(stock: 3);
        var empty = await CreateMedicineAsync(stock: 0);
        var prescriptionId = FakePrescriptionService.Register(ok.Name, empty.Name, "Yok " + Guid.NewGuid().ToString("N")[..6]);

        var response = await Client.GetAsync($"/api/pharmacy/dispense/preview/{prescriptionId}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await ReadAsync<DispensePreviewDto>(response);
        Assert.False(preview.CanDispense);
        Assert.False(preview.AlreadyDispensed);
        Assert.Equal(
            new[] { DispenseLineStatus.Matched, DispenseLineStatus.InsufficientStock, DispenseLineStatus.NotFound },
            preview.Lines.OrderBy(l => l.LineNo).Select(l => l.Status));
        Assert.Equal(ok.Id, preview.Lines[0].MedicineId);
        Assert.Equal(3, preview.Lines[0].Available);
        Assert.Equal(3, await StockAsync(ok));
        Assert.False(await WasDispensedAsync(prescriptionId));
    }

    [Fact]
    public async Task Preview_BecomesAlreadyDispensedAfterDispensing()
    {
        var medicine = await CreateMedicineAsync(stock: 3);
        var prescriptionId = FakePrescriptionService.Register(medicine.Name);

        var before = await ReadAsync<DispensePreviewDto>(await Client.GetAsync($"/api/pharmacy/dispense/preview/{prescriptionId}", Ct));
        await DispenseAsync(prescriptionId);
        var after = await ReadAsync<DispensePreviewDto>(await Client.GetAsync($"/api/pharmacy/dispense/preview/{prescriptionId}", Ct));

        Assert.True(before.CanDispense);
        Assert.False(after.CanDispense);
        Assert.True(after.AlreadyDispensed);
    }

    [Fact]
    public async Task Preview_UnknownPrescription_Returns404_AndServiceDownReturns503()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/pharmacy/dispense/preview/{Guid.CreateVersion7()}", Ct)).StatusCode);

        FakePrescriptionService.Mode = PrescriptionMode.Down;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Client.GetAsync($"/api/pharmacy/dispense/preview/{Guid.CreateVersion7()}", Ct)).StatusCode);
    }

    // --- Kayıt listeleri ---

    [Fact]
    public async Task Dispensations_ListIsPaged_NewestFirst_AndByPrescriptionLookupWorks()
    {
        var medicine = await CreateMedicineAsync(stock: 10);
        var first = FakePrescriptionService.Register(medicine.Name);
        var second = FakePrescriptionService.Register(medicine.Name);
        await DispenseAsync(first);
        await Task.Delay(20, Ct);
        await DispenseAsync(second);

        var page = await ReadAsync<PagedResult<DispensationDto>>(await Client.GetAsync("/api/pharmacy/dispensations?page=1&pageSize=2", Ct));

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(second, page.Items[0].PrescriptionId);
        Assert.True(page.TotalCount >= 2);

        var single = await ReadAsync<DispensationDto>(await Client.GetAsync($"/api/pharmacy/dispensations/by-prescription/{first}", Ct));
        Assert.Equal(first, single.PrescriptionId);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/pharmacy/dispensations/by-prescription/{Guid.CreateVersion7()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.GetAsync("/api/pharmacy/dispensations?page=0", Ct)).StatusCode);
    }
}
