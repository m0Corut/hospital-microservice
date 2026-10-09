using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hastane.Prescription.Api.Data;
using Hastane.Prescription.Api.Dtos;
using Hastane.Prescription.Api.Services;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Prescription.Tests;

public class PrescriptionsApiTests : IClassFixture<PrescriptionApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PrescriptionApiFactory _factory;
    private readonly HttpClient _client;

    public PrescriptionsApiTests(PrescriptionApiFactory factory)
    {
        _factory = factory;
        // Mevcut testler "herkes adına yazabilen" Admin ile çalışır: gövdedeki DoctorId/PatientId ile senaryo kurmaya
        // devam eder ve her reçeteyi görür/değiştirir. Yetki kuralları PrescriptionAuthorizationTests'te.
        _client = factory.CreateClient().WithBearer(TestJwt.Create("admin-1", [HastaneRoles.Admin]));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Her test kendi hasta/doktoruyla çalışır: aynı konteynerdeki başka testlerin verisi süzgeçlere karışmaz.
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static CreatePrescriptionItemDto NewItem(string name = "Parol") => new()
    {
        MedicineName = name,
        Dosage = "500 mg",
        Frequency = "günde 3 kez",
        DurationDays = 5,
        Instructions = "Tok karnına",
    };

    private static CreatePrescriptionDto NewPrescription(
        string? doctorId = null, string? patientId = null, Guid? appointmentId = null, int itemCount = 2) => new()
        {
            AppointmentId = appointmentId ?? Guid.NewGuid(),
            DoctorId = doctorId ?? Unique("doc"),
            PatientId = patientId ?? Unique("pat"),
            Notes = "Bol sıvı tüketin",
            Items = Enumerable.Range(1, itemCount).Select(i => NewItem($"İlaç {i}")).ToList(),
        };

    private async Task<GetByIdPrescriptionDto> CreateAsync(CreatePrescriptionDto? dto = null)
    {
        var response = await _client.PostAsJsonAsync("/api/prescriptions", dto ?? NewPrescription(), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<GetByIdPrescriptionDto>(Json, Ct))!;
    }

    private async Task<List<ResultPrescriptionDto>> ListAsync(string query)
        => (await _client.GetFromJsonAsync<List<ResultPrescriptionDto>>($"/api/prescriptions?{query}", Json, Ct))!;

    private async Task<(int Prescriptions, int Items)> CountRowsAsync(Guid? prescriptionId = null, string? patientId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PrescriptionDbContext>();
        var prescriptions = await db.Prescriptions.CountAsync(
            p => (patientId == null || p.PatientId == patientId) && (prescriptionId == null || p.Id == prescriptionId), Ct);
        var items = await db.PrescriptionItems.CountAsync(
            i => prescriptionId == null || i.PrescriptionId == prescriptionId, Ct);
        return (prescriptions, items);
    }

    // --- Oluşturma ---

    [Fact]
    public async Task Create_WritesPrescriptionAndAllItems_AssignsIdsAndIssuedAtOnServer()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var dto = NewPrescription(itemCount: 3);

        var created = await CreateAsync(dto);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(DateTimeKind.Utc, created.IssuedAt.Kind);
        Assert.InRange(created.IssuedAt, before, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(3, created.Items.Count);
        Assert.All(created.Items, i => Assert.NotEqual(Guid.Empty, i.Id));
        Assert.Equal(3, created.Items.Select(i => i.Id).Distinct().Count());
        Assert.Equal((1, 3), await CountRowsAsync(created.Id));
    }

    [Fact]
    public async Task Create_IgnoresClientSuppliedIdAndIssuedAt()
    {
        var fakeId = Guid.NewGuid();
        var body = new
        {
            id = fakeId,
            issuedAt = "2001-01-01T00:00:00Z",
            appointmentId = Guid.NewGuid(),
            doctorId = Unique("doc"),
            patientId = Unique("pat"),
            items = new[] { NewItem() },
        };

        var response = await _client.PostAsJsonAsync("/api/prescriptions", body, Json, Ct);
        var created = (await response.Content.ReadFromJsonAsync<GetByIdPrescriptionDto>(Json, Ct))!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEqual(fakeId, created.Id);
        Assert.True(created.IssuedAt.Year >= 2026);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Create_ItemCountOutOfRange_ReturnsBadRequest_AndWritesNothing(int itemCount)
    {
        var dto = NewPrescription(itemCount: itemCount);

        var response = await _client.PostAsJsonAsync("/api/prescriptions", dto, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await CountRowsAsync(patientId: dto.PatientId)).Prescriptions);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public async Task Create_ItemCountBoundaries_Accepted(int itemCount)
    {
        var created = await CreateAsync(NewPrescription(itemCount: itemCount));
        Assert.Equal(itemCount, created.Items.Count);
    }

    [Fact]
    public async Task Create_InvalidItem_ReturnsBadRequest_AndWritesNoRows()
    {
        var dto = NewPrescription(itemCount: 2);
        dto.Items[1].DurationDays = 0; // 1. kalem geçerli, 2. geçersiz: geçerli olan da yazılmamalı
        var dto2 = NewPrescription(itemCount: 2);
        dto2.Items[1].MedicineName = "";
        var dto3 = NewPrescription(itemCount: 1);
        dto3.Items[0].DurationDays = 366;

        foreach (var bad in new[] { dto, dto2, dto3 })
        {
            var response = await _client.PostAsJsonAsync("/api/prescriptions", bad, Json, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, (await CountRowsAsync(patientId: bad.PatientId)).Prescriptions);
        }
    }

    [Fact]
    public async Task Create_MissingRequiredFields_ReturnsBadRequest()
    {
        var empty = NewPrescription();
        empty.AppointmentId = Guid.Empty;
        var noDoctor = NewPrescription(doctorId: "");

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/prescriptions", empty, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/prescriptions", noDoctor, Json, Ct)).StatusCode);
    }

    // Ya hep ya hiç: doğrulamayı atlayıp doğrudan servise geçersiz (veritabanının reddedeceği) kalem veriyoruz.
    // 2. kalemin adı 200 karakterden uzun: SQL Server SaveChanges'te hata verir. Geçerli reçete satırı
    // ve 1. kalem de geri alınır, tabloda hiçbir şey kalmaz.
    [Fact]
    public async Task Create_WhenDatabaseRejectsAnyRow_NothingIsPersisted_AllOrNothing()
    {
        var dto = NewPrescription(itemCount: 2);
        dto.Items[1].MedicineName = new string('x', 201);

        using var scope = _factory.Services.CreateScope();
        // HTTP dışı çağrıda HttpContext yok; "kullanıcı" olarak Admin verilir (doğrulamayı değil veritabanı reddini sınıyoruz).
        var service = new PrescriptionService(
            scope.ServiceProvider.GetRequiredService<PrescriptionDbContext>(),
            scope.ServiceProvider.GetRequiredService<MapsterMapper.IMapper>(),
            new StubAdminUser());

        await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(dto, Ct));

        var db = scope.ServiceProvider.GetRequiredService<PrescriptionDbContext>();
        db.ChangeTracker.Clear();
        Assert.False(await db.Prescriptions.AnyAsync(p => p.PatientId == dto.PatientId, Ct));
        Assert.False(await db.PrescriptionItems.AnyAsync(i => i.MedicineName.StartsWith("İlaç 1") && i.Prescription!.PatientId == dto.PatientId, Ct));
    }

    // --- Okuma ---

    [Fact]
    public async Task GetById_ReturnsPrescriptionWithItems()
    {
        var created = await CreateAsync(NewPrescription(itemCount: 3));

        var response = await _client.GetAsync($"/api/prescriptions/{created.Id}", Ct);
        var fetched = (await response.Content.ReadFromJsonAsync<GetByIdPrescriptionDto>(Json, Ct))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal(created.AppointmentId, fetched.AppointmentId);
        Assert.Equal(3, fetched.Items.Count);
        var first = fetched.Items.Single(i => i.MedicineName == "İlaç 1");
        Assert.Equal("500 mg", first.Dosage);
        Assert.Equal("günde 3 kez", first.Frequency);
        Assert.Equal(5, first.DurationDays);
        Assert.Equal("Tok karnına", first.Instructions);
        Assert.Equal(DateTimeKind.Utc, fetched.IssuedAt.Kind);
    }

    [Fact]
    public async Task GetById_Unknown_ReturnsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/prescriptions/{Guid.NewGuid()}", Ct)).StatusCode);
    }

    [Fact]
    public async Task MalformedGuid_ReturnsNotFound_NotServerError()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/prescriptions/abc", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/prescriptions/abc", Ct)).StatusCode);
        var put = await _client.PutAsJsonAsync("/api/prescriptions/abc", new UpdatePrescriptionDto { Id = Guid.NewGuid(), Items = [NewItem()] }, Json, Ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsItemCount_WithoutItems_NewestFirst()
    {
        var patient = Unique("pat");
        var older = await CreateAsync(NewPrescription(patientId: patient, itemCount: 2));
        await Task.Delay(20, Ct);
        var newer = await CreateAsync(NewPrescription(patientId: patient, itemCount: 4));

        var list = await ListAsync($"patientId={patient}");

        Assert.Equal([newer.Id, older.Id], list.Select(p => p.Id));
        Assert.Equal([4, 2], list.Select(p => p.ItemCount));

        // Ham JSON'da kalem alanı olmamalı (liste uçları kalemleri yüklemez).
        var raw = await _client.GetStringAsync($"/api/prescriptions?patientId={patient}", Ct);
        Assert.DoesNotContain("items", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("medicineName", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_Filters_ByPatientDoctorAppointment_AndCombined()
    {
        var doctor = Unique("doc");
        var patient = Unique("pat");
        var appointment = Guid.NewGuid();
        var target = await CreateAsync(NewPrescription(doctor, patient, appointment));
        await CreateAsync(NewPrescription(doctor, Unique("pat")));
        await CreateAsync(NewPrescription(Unique("doc"), patient));

        Assert.Equal(2, (await ListAsync($"doctorId={doctor}")).Count);
        Assert.Equal(2, (await ListAsync($"patientId={patient}")).Count);
        Assert.Equal(target.Id, Assert.Single(await ListAsync($"appointmentId={appointment}")).Id);
        Assert.Equal(target.Id, Assert.Single(await ListAsync($"doctorId={doctor}&patientId={patient}&appointmentId={appointment}")).Id);
        Assert.Empty(await ListAsync($"doctorId={doctor}&patientId={Unique("none")}"));
    }

    // --- Güncelleme ---

    [Fact]
    public async Task Update_ReplacesItemsCompletely_OldItemIdsGone()
    {
        var created = await CreateAsync(NewPrescription(itemCount: 3));
        var oldIds = created.Items.Select(i => i.Id).ToHashSet();
        var update = new UpdatePrescriptionDto
        {
            Id = created.Id,
            Notes = "güncel",
            Items = [NewItem("Yeni A"), NewItem("Yeni B")],
        };

        var response = await _client.PutAsJsonAsync($"/api/prescriptions/{created.Id}", update, Json, Ct);
        var updated = (await response.Content.ReadFromJsonAsync<GetByIdPrescriptionDto>(Json, Ct))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("güncel", updated.Notes);
        Assert.Equal(2, updated.Items.Count);

        var fetched = (await _client.GetFromJsonAsync<GetByIdPrescriptionDto>($"/api/prescriptions/{created.Id}", Json, Ct))!;
        Assert.Equal(["Yeni A", "Yeni B"], fetched.Items.Select(i => i.MedicineName).OrderBy(n => n));
        Assert.DoesNotContain(fetched.Items, i => oldIds.Contains(i.Id));
        // Reçetenin değişmeyen alanları yerinde.
        Assert.Equal(created.DoctorId, fetched.DoctorId);
        Assert.Equal(created.PatientId, fetched.PatientId);
        Assert.Equal(created.AppointmentId, fetched.AppointmentId);
        Assert.Equal(created.IssuedAt, fetched.IssuedAt);
        Assert.Equal((1, 2), await CountRowsAsync(created.Id));
    }

    [Fact]
    public async Task Update_IdMismatch_ReturnsBadRequest()
    {
        var created = await CreateAsync();
        var update = new UpdatePrescriptionDto { Id = Guid.NewGuid(), Items = [NewItem()] };

        var response = await _client.PutAsJsonAsync($"/api/prescriptions/{created.Id}", update, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Unknown_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        var response = await _client.PutAsJsonAsync($"/api/prescriptions/{id}", new UpdatePrescriptionDto { Id = id, Items = [NewItem()] }, Json, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_InvalidItems_ReturnsBadRequest_AndKeepsOldItems()
    {
        var created = await CreateAsync(NewPrescription(itemCount: 2));
        var empty = new UpdatePrescriptionDto { Id = created.Id, Items = [] };
        var tooMany = new UpdatePrescriptionDto { Id = created.Id, Items = Enumerable.Range(0, 21).Select(_ => NewItem()).ToList() };
        var badItem = new UpdatePrescriptionDto { Id = created.Id, Items = [new CreatePrescriptionItemDto { MedicineName = "", Dosage = "1", Frequency = "f", DurationDays = 1 }] };

        foreach (var bad in new[] { empty, tooMany, badItem })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync($"/api/prescriptions/{created.Id}", bad, Json, Ct)).StatusCode);

        var fetched = (await _client.GetFromJsonAsync<GetByIdPrescriptionDto>($"/api/prescriptions/{created.Id}", Json, Ct))!;
        Assert.Equal(created.Items.Select(i => i.Id).Order(), fetched.Items.Select(i => i.Id).Order());
    }

    // --- Silme ---

    [Fact]
    public async Task Delete_RemovesPrescription_AndItsItemsByCascade()
    {
        var created = await CreateAsync(NewPrescription(itemCount: 3));
        Assert.Equal((1, 3), await CountRowsAsync(created.Id));

        var response = await _client.DeleteAsync($"/api/prescriptions/{created.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/prescriptions/{created.Id}", Ct)).StatusCode);
        Assert.Equal((0, 0), await CountRowsAsync(created.Id));
    }

    [Fact]
    public async Task Delete_Unknown_ReturnsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/prescriptions/{Guid.NewGuid()}", Ct)).StatusCode);
    }

    // --- Sınırlar, LineNo ve servis katmanı doğrulaması ---

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public async Task Create_DurationDaysBoundaries_Accepted(int days)
    {
        var dto = NewPrescription(itemCount: 1);
        dto.Items[0].DurationDays = days;
        var created = await CreateAsync(dto);
        Assert.Equal(days, created.Items[0].DurationDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Update_ItemCountOutOfRange_ReturnsBadRequest_AndKeepsOldItems(int itemCount)
    {
        var created = await CreateAsync(NewPrescription(itemCount: 2));
        var update = new UpdatePrescriptionDto
        {
            Id = created.Id,
            Items = Enumerable.Range(0, itemCount).Select(_ => NewItem()).ToList(),
        };

        var response = await _client.PutAsJsonAsync($"/api/prescriptions/{created.Id}", update, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((1, 2), await CountRowsAsync(created.Id));
    }

    [Fact]
    public async Task Update_EmptyGuidId_ReturnsBadRequest()
    {
        var update = new UpdatePrescriptionDto { Id = Guid.Empty, Items = [NewItem()] };
        var response = await _client.PutAsJsonAsync($"/api/prescriptions/{Guid.Empty}", update, Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NumbersItemsInSubmittedOrder_NotAlphabetical()
    {
        var dto = NewPrescription(itemCount: 0);
        dto.Items = new[] { "Zeta", "Mu", "Alfa", "Beta" }.Select(n => NewItem(n)).ToList();

        var created = await CreateAsync(dto);

        Assert.Equal([1, 2, 3, 4], created.Items.Select(i => i.LineNo));
        Assert.Equal(["Zeta", "Mu", "Alfa", "Beta"], created.Items.Select(i => i.MedicineName));

        var fetched = (await _client.GetFromJsonAsync<GetByIdPrescriptionDto>($"/api/prescriptions/{created.Id}", Json, Ct))!;
        Assert.Equal([1, 2, 3, 4], fetched.Items.Select(i => i.LineNo));
        Assert.Equal(["Zeta", "Mu", "Alfa", "Beta"], fetched.Items.Select(i => i.MedicineName));
    }

    [Fact]
    public async Task Update_RenumbersNewItemsFromOne_InSubmittedOrder()
    {
        var created = await CreateAsync(NewPrescription(itemCount: 3));
        var update = new UpdatePrescriptionDto { Id = created.Id, Items = [NewItem("Z"), NewItem("B")] };

        var response = await _client.PutAsJsonAsync($"/api/prescriptions/{created.Id}", update, Json, Ct);
        var updated = (await response.Content.ReadFromJsonAsync<GetByIdPrescriptionDto>(Json, Ct))!;
        var fetched = (await _client.GetFromJsonAsync<GetByIdPrescriptionDto>($"/api/prescriptions/{created.Id}", Json, Ct))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var dto in new[] { updated, fetched })
        {
            Assert.Equal([1, 2], dto.Items.Select(i => i.LineNo));
            Assert.Equal(["Z", "B"], dto.Items.Select(i => i.MedicineName));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task Service_RejectsInvalidItemCount_EvenWhenDtoValidationIsBypassed(int itemCount)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPrescriptionService>();
        var dto = NewPrescription(itemCount: itemCount);

        var create = await service.CreateAsync(dto, Ct);
        Assert.Equal(OperationStatus.Validation, create.Status);
        Assert.Equal(0, (await CountRowsAsync(patientId: dto.PatientId)).Prescriptions);

        var existing = await CreateAsync();
        var update = await service.UpdateAsync(new UpdatePrescriptionDto
        {
            Id = existing.Id,
            Items = Enumerable.Range(0, itemCount).Select(_ => NewItem()).ToList(),
        }, Ct);
        Assert.Equal(OperationStatus.Validation, update.Status);
        Assert.Equal((1, 2), await CountRowsAsync(existing.Id));
    }
}

// Servisi HTTP dışında doğrudan çağıran testler için sabit Admin kullanıcı.
internal sealed class StubAdminUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public string? UserId => "admin-stub";
    public string? Email => null;
    public string? DoctorId => null;
    public IReadOnlyList<string> Roles => [HastaneRoles.Admin];
    public bool IsAdmin => true;
    public bool IsInRole(string role) => role == HastaneRoles.Admin;
}
