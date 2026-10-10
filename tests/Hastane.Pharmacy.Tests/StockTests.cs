using System.Net;
using Hastane.Pharmacy.Api.Common;
using Hastane.Pharmacy.Api.Entities;
using Hastane.Pharmacy.Api.Features.Medicines;
using Hastane.Pharmacy.Api.Features.Medicines.Commands.AdjustStock;
using Hastane.Pharmacy.Api.Features.Medicines.Commands.CreateMedicine;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Pharmacy.Tests;

[Collection(PharmacyCollection.Name)]
public class StockTests : PharmacyTestBase
{
    public StockTests(PharmacyApiFactory factory) : base(factory)
    {
    }

    private Task<int> StockInDbAsync(Guid id) => WithDbAsync(db => db.Medicines.Where(m => m.Id == id).Select(m => m.Stock).SingleAsync(Ct));

    private Task<int> MovementCountAsync(Guid id) => WithDbAsync(db => db.StockMovements.CountAsync(s => s.MedicineId == id, Ct));

    // --- Temel davranış ---

    [Fact]
    public async Task Adjust_AddsAndRemoves_AndRecordsMovementWithStockAfter()
    {
        var medicine = await CreateMedicineAsync(stock: 10);

        var inbound = await AdjustAsync(medicine.Id, +5, "mal kabul");
        var outbound = await AdjustAsync(medicine.Id, -12, "sayım farkı");

        Assert.Equal(HttpStatusCode.OK, inbound.StatusCode);
        Assert.Equal(15, (await ReadAsync<StockMovementDto>(inbound)).StockAfter);
        var last = await ReadAsync<StockMovementDto>(outbound);
        Assert.Equal(3, last.StockAfter);
        Assert.Equal(-12, last.Delta);
        Assert.Equal("sayım farkı", last.Reason);
        Assert.Equal(3, (await GetMedicineAsync(medicine.Id)).Stock);
        Assert.Equal(3, await MovementCountAsync(medicine.Id)); // ilk stok + 2 düzeltme
    }

    [Fact]
    public async Task Adjust_BelowZero_Returns409_StockAndLedgerUnchanged()
    {
        var medicine = await CreateMedicineAsync(stock: 3);

        var response = await AdjustAsync(medicine.Id, -4, "fazla çıkış");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(3, await StockInDbAsync(medicine.Id));
        Assert.Equal(1, await MovementCountAsync(medicine.Id)); // yalnız ilk stok kaydı
    }

    [Fact]
    public async Task Adjust_ExactlyToZero_IsAllowed()
    {
        var medicine = await CreateMedicineAsync(stock: 3);
        Assert.Equal(HttpStatusCode.OK, (await AdjustAsync(medicine.Id, -3)).StatusCode);
        Assert.Equal(0, await StockInDbAsync(medicine.Id));
    }

    [Fact]
    public async Task Adjust_UnknownOrDeletedMedicine_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await AdjustAsync(Guid.NewGuid(), +1)).StatusCode);

        var medicine = await CreateMedicineAsync();
        await Client.DeleteAsync($"/api/medicines/{medicine.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, (await AdjustAsync(medicine.Id, +1)).StatusCode);
    }

    [Fact]
    public async Task Adjust_InvalidBody_Returns400_AndChangesNothing()
    {
        var medicine = await CreateMedicineAsync(stock: 5);

        Assert.Equal(HttpStatusCode.BadRequest, (await AdjustAsync(medicine.Id, 0, "sıfır")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AdjustAsync(medicine.Id, 3, "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AdjustAsync(medicine.Id, 3, new string('x', 201))).StatusCode);
        Assert.Equal(5, await StockInDbAsync(medicine.Id));
    }

    // --- Eşzamanlılık: kayıp güncelleme yok ---

    [Fact]
    public async Task ConcurrentDecrements_NeverGoNegative_ExactlyStockManySucceed()
    {
        var medicine = await CreateMedicineAsync(stock: 10);

        var responses = await Task.WhenAll(Enumerable.Range(0, 25).Select(_ => AdjustAsync(medicine.Id, -1, "eşzamanlı")));

        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(15, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await StockInDbAsync(medicine.Id));
        Assert.Equal(1 + 10, await MovementCountAsync(medicine.Id)); // her başarılı düşüm tam bir hareket
    }

    [Fact]
    public async Task ConcurrentIncrements_LoseNoUpdates()
    {
        var medicine = await CreateMedicineAsync(stock: 0);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => AdjustAsync(medicine.Id, +1, "giriş")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(20, await StockInDbAsync(medicine.Id));
        Assert.Equal(20, await MovementCountAsync(medicine.Id));
    }

    [Fact]
    public async Task ConcurrentMixedChanges_LedgerSumMatchesStock()
    {
        var medicine = await CreateMedicineAsync(stock: 50);

        var tasks = Enumerable.Range(0, 30).Select(i => AdjustAsync(medicine.Id, i % 2 == 0 ? +3 : -2, "karışık"));
        await Task.WhenAll(tasks);

        var stock = await StockInDbAsync(medicine.Id);
        var ledgerSum = await WithDbAsync(db => db.StockMovements.Where(s => s.MedicineId == medicine.Id).SumAsync(s => s.Delta, Ct));
        Assert.Equal(stock, ledgerSum); // defter ile stok her zaman tutuyor
        Assert.Equal(50 + 15 * 3 - 15 * 2, stock);
    }

    [Fact]
    public async Task Database_RejectsNegativeStock_EvenIfCodeForgotTheCheck()
    {
        var medicine = await CreateMedicineAsync(stock: 1);

        await Assert.ThrowsAnyAsync<SqlException>(() => WithDbAsync(db =>
            db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Medicines SET Stock = -1 WHERE Id = {medicine.Id}", Ct)));

        Assert.Equal(1, await StockInDbAsync(medicine.Id));
    }

    // --- Handler'ı HTTP'siz çağırmak: aynı hat (pipeline + handler + gerçek SQL) ---

    [Fact]
    public async Task Handler_ThroughSender_ReturnsConflictResult_NotAnException()
    {
        using var scope = Factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var created = await sender.Send(new CreateMedicineCommand(UniqueName(), "Etken", MedicineForm.Syrup, "5 ml", NewBarcode(), 10m, 2, 1, false), Ct);
        Assert.Equal(OperationStatus.Created, created.Status);

        var result = await sender.Send(new AdjustStockCommand(created.Value!.Id, -3, "fazla"), Ct);

        Assert.Equal(OperationStatus.Conflict, result.Status);
        Assert.Equal(2, (await sender.Send(new Api.Features.Medicines.Queries.GetMedicineById.GetMedicineByIdQuery(created.Value.Id), Ct)).Value!.Stock);
    }

    [Fact]
    public async Task Handler_ThroughSender_InvalidCommand_ThrowsValidationBeforeAnyDatabaseWork()
    {
        using var scope = Factory.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var barcode = NewBarcode();

        await Assert.ThrowsAsync<Api.Behaviors.RequestValidationException>(() =>
            sender.Send(new CreateMedicineCommand("", "Etken", MedicineForm.Tablet, "1 mg", barcode, 1m, 0, 0, false), Ct));

        Assert.False(await WithDbAsync(db => db.Medicines.AnyAsync(m => m.Barcode == barcode, Ct)));
    }
}
