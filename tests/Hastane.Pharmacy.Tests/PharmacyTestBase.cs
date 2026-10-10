using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Pharmacy.Api.Data;
using Hastane.Pharmacy.Api.Features.Medicines;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Pharmacy.Tests;

public abstract class PharmacyTestBase : IDisposable
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected readonly PharmacyApiFactory Factory;
    protected readonly HttpClient Client;
    protected readonly string AdminJwt = TestJwt.Create("admin-1", [HastaneRoles.Admin]);

    protected PharmacyTestBase(PharmacyApiFactory factory)
    {
        Factory = factory;
        // Varsayılan istemci Admin: çoğu test yönetici işlemini sınar. Rol kuralları PharmacyAuthorizationTests'te.
        Client = factory.CreateClient().WithBearer(AdminJwt);
        FakePrescriptionService.Reset();
    }

    public void Dispose() => FakePrescriptionService.Reset();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected HttpClient ClientFor(string? token = null)
    {
        var client = Factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    // Barkod: en çok 20 karakter, yalnız harf/rakam.
    protected static string NewBarcode() => Guid.NewGuid().ToString("N")[..16];

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct));

    // Her test kendi benzersiz adını üretir (paylaşılan veritabanında çakışma olmasın). Ad "Marka-xxxxxxxx" biçiminde.
    protected static string UniqueName(string brand = "Parasetamol") => $"{brand} {Guid.NewGuid():N}"[..(brand.Length + 9)];

    protected static object NewMedicineBody(
        string? name = null, int stock = 10, int minStock = 5, string form = "Tablet", string? barcode = null,
        decimal price = 25.50m, string ingredient = "Parasetamol", bool requiresPrescription = false)
        => new
        {
            name = name ?? UniqueName(),
            activeIngredient = ingredient,
            form,
            strength = "500 mg",
            barcode = barcode ?? NewBarcode(),
            price,
            stock,
            minStock,
            requiresPrescription,
        };

    protected async Task<MedicineDto> CreateMedicineAsync(
        string? name = null, int stock = 10, int minStock = 5, string form = "Tablet", string? barcode = null, decimal price = 25.50m)
    {
        var response = await Client.PostAsJsonAsync("/api/medicines", NewMedicineBody(name, stock, minStock, form, barcode, price), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<MedicineDto>(response);
    }

    protected async Task<MedicineDto> GetMedicineAsync(Guid id)
    {
        var response = await Client.GetAsync($"/api/medicines/{id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<MedicineDto>(response);
    }

    protected Task<HttpResponseMessage> AdjustAsync(Guid id, int delta, string reason = "test")
        => Client.PostAsJsonAsync($"/api/medicines/{id}/stock", new { delta, reason }, Json, Ct);

    protected Task<HttpResponseMessage> DispenseAsync(Guid prescriptionId, HttpClient? client = null)
        => (client ?? Client).PostAsJsonAsync("/api/pharmacy/dispense", new { prescriptionId }, Json, Ct);

    // Doğrudan veritabanına bakmak için kısa ömürlü bir kapsam.
    protected async Task<T> WithDbAsync<T>(Func<PharmacyDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<PharmacyDbContext>());
    }
}
