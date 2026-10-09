using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Appointment.Api.Data;
using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Appointment.Tests;

public class AppointmentsApiTests : IClassFixture<AppointmentApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppointmentApiFactory _factory;
    private readonly HttpClient _client;

    public AppointmentsApiTests(AppointmentApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Her test kendi doktoruyla çalışır: aynı konteynerdeki başka testlerin verisi süzgeçlere karışmaz.
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static DateTime Future(int days = 5) => DateTime.UtcNow.AddDays(days);

    // Dakika sınırına oturan sabit bir gelecek slot (taban: 40 gün sonra). offsetMinutes ile komşu slotlar.
    private static readonly DateTime SlotBase = TruncateToMinute(DateTime.UtcNow.AddDays(40));

    private static DateTime TruncateToMinute(DateTime value)
        => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    private static DateTime Slot(int offsetMinutes) => SlotBase.AddMinutes(offsetMinutes);

    private static CreateAppointmentDto NewAppointment(string? doctorId = null, string? patientId = null, DateTime? date = null) => new()
    {
        DoctorId = doctorId ?? Unique("doc"),
        PatientId = patientId ?? Unique("pat"),
        BranchId = "branch-1",
        AppointmentDate = date ?? Future(),
    };

    private async Task<ResultAppointmentDto> CreateAsync(CreateAppointmentDto? dto = null)
    {
        var response = await _client.PostAsJsonAsync("/api/appointments", dto ?? NewAppointment(), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;
    }

    private Task<HttpResponseMessage> PatchStatusAsync(Guid id, AppointmentStatus status)
        => _client.PatchAsJsonAsync($"/api/appointments/{id}/status", new UpdateStatusDto { Status = status }, Json, Ct);

    private static AppointmentStatus[] PathTo(AppointmentStatus target) => target switch
    {
        AppointmentStatus.Pending => [],
        AppointmentStatus.Confirmed => [AppointmentStatus.Confirmed],
        AppointmentStatus.Cancelled => [AppointmentStatus.Cancelled],
        _ => [AppointmentStatus.Confirmed, AppointmentStatus.Completed],
    };

    private async Task<ResultAppointmentDto> CreateInStatusAsync(AppointmentStatus status, CreateAppointmentDto? dto = null)
    {
        var created = await CreateAsync(dto);
        foreach (var step in PathTo(status))
            Assert.Equal(HttpStatusCode.OK, (await PatchStatusAsync(created.Id, step)).StatusCode);
        return created;
    }

    private static string Iso(DateTime value) => Uri.EscapeDataString(value.ToString("O"));

    private async Task<List<ResultAppointmentDto>> ListAsync(string query)
        => (await _client.GetFromJsonAsync<List<ResultAppointmentDto>>($"/api/appointments?{query}", Json, Ct))!;

    // --- Oluşturma ---

    [Fact]
    public async Task Create_AssignsPendingStatusIdAndCreatedAtOnServer_IgnoringClientValues()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var date = Future(10);
        // İstemci sahte Id/Status/CreatedAt göndermeye çalışır; DTO'da bu alanlar yok, gövdedeki fazlalık yok sayılır.
        var body = $$"""
            {"doctorId":"d-srv","patientId":"p-srv","branchId":"b","appointmentDate":"{{date:O}}",
             "id":"11111111-1111-1111-1111-111111111111","status":"Completed","createdAt":"2000-01-01T00:00:00Z"}
            """;
        var response = await _client.PostAsync("/api/appointments", new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.Equal(AppointmentStatus.Pending, created.Status);
        Assert.NotEqual(Guid.Parse("11111111-1111-1111-1111-111111111111"), created.Id);
        Assert.Equal(7, created.Id.Version);
        Assert.InRange(created.CreatedAt, before, after);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
    }

    [Fact]
    public async Task Create_PastDate_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/appointments", NewAppointment(date: DateTime.UtcNow.AddMinutes(-5)), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_MissingIds_Returns400()
    {
        var dto = NewAppointment();
        dto.DoctorId = "";
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/appointments", dto, Json, Ct)).StatusCode);
        dto = NewAppointment();
        dto.PatientId = "";
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/appointments", dto, Json, Ct)).StatusCode);
        dto = NewAppointment();
        dto.BranchId = "";
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/appointments", dto, Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Create_ThenGetById_ReturnsDetailNull()
    {
        var created = await CreateAsync();
        var fetched = await _client.GetFromJsonAsync<GetByIdAppointmentDto>($"/api/appointments/{created.Id}", Json, Ct);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(created.DoctorId, fetched.DoctorId);
        Assert.Null(fetched.Detail);
    }

    [Fact]
    public async Task Status_IsStoredAsStringInDatabase()
    {
        var created = await CreateInStatusAsync(AppointmentStatus.Confirmed);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        var raw = await db.Database
            .SqlQueryRaw<string>("SELECT Status AS Value FROM Appointments WHERE Id = {0}", created.Id)
            .SingleAsync(Ct);
        Assert.Equal("Confirmed", raw);
    }

    // --- Listeleme / filtre ---

    [Fact]
    public async Task List_FiltersByDoctorPatientAndStatus_AndCombines()
    {
        var doctor = Unique("doc");
        var patientA = Unique("pat");
        var patientB = Unique("pat");
        // Aynı doktor: farklı dakikalar (aynı doktor + aynı dakika artık 409).
        var pendingA = await CreateAsync(NewAppointment(doctor, patientA, Slot(0)));
        var confirmedB = await CreateInStatusAsync(AppointmentStatus.Confirmed, NewAppointment(doctor, patientB, Slot(1)));
        await CreateAsync(NewAppointment(Unique("doc"), patientA)); // başka doktor, aynı hasta

        var byDoctor = await ListAsync($"doctorId={doctor}");
        Assert.Equal(new[] { pendingA.Id, confirmedB.Id }.Order(), byDoctor.Select(a => a.Id).Order());

        var byPatient = await ListAsync($"patientId={patientA}");
        Assert.Equal(2, byPatient.Count);

        var byStatus = await ListAsync($"doctorId={doctor}&status=Confirmed");
        Assert.Equal(confirmedB.Id, Assert.Single(byStatus).Id);

        var combined = await ListAsync($"doctorId={doctor}&patientId={patientA}&status=Pending");
        Assert.Equal(pendingA.Id, Assert.Single(combined).Id);

        Assert.Empty(await ListAsync($"doctorId={doctor}&patientId={patientA}&status=Confirmed"));
    }

    [Fact]
    public async Task List_FiltersByDateRange_AndSortsAscending()
    {
        var doctor = Unique("doc");
        var baseDate = DateTime.UtcNow.AddDays(30);
        var late = await CreateAsync(NewAppointment(doctor, date: baseDate.AddDays(10)));
        var early = await CreateAsync(NewAppointment(doctor, date: baseDate));
        var middle = await CreateAsync(NewAppointment(doctor, date: baseDate.AddDays(5)));

        var all = await ListAsync($"doctorId={doctor}");
        Assert.Equal(new[] { early.Id, middle.Id, late.Id }, all.Select(a => a.Id));

        var from = await ListAsync($"doctorId={doctor}&from={Iso(baseDate.AddDays(3))}");
        Assert.Equal(new[] { middle.Id, late.Id }, from.Select(a => a.Id));

        var to = await ListAsync($"doctorId={doctor}&to={Iso(baseDate.AddDays(3))}");
        Assert.Equal(early.Id, Assert.Single(to).Id);

        var range = await ListAsync($"doctorId={doctor}&from={Iso(baseDate.AddDays(3))}&to={Iso(baseDate.AddDays(7))}");
        Assert.Equal(middle.Id, Assert.Single(range).Id);
    }

    [Fact]
    public async Task List_InvalidStatus_Returns400()
        => Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/appointments?status=Bogus", Ct)).StatusCode);

    // --- Yeniden planlama ---

    [Fact]
    public async Task Reschedule_ChangesOnlyBranchAndDate()
    {
        var created = await CreateAsync();
        var newDate = Future(20);
        var response = await _client.PutAsJsonAsync($"/api/appointments/{created.Id}",
            new UpdateAppointmentDto { Id = created.Id, BranchId = "branch-2", AppointmentDate = newDate }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var fetched = (await _client.GetFromJsonAsync<GetByIdAppointmentDto>($"/api/appointments/{created.Id}", Json, Ct))!;
        Assert.Equal("branch-2", fetched.BranchId);
        // Dakika hassasiyeti: saniye ve altı sunucuda sıfırlanır.
        Assert.Equal(TruncateToMinute(newDate), fetched.AppointmentDate);
        Assert.Equal(created.DoctorId, fetched.DoctorId);
        Assert.Equal(created.PatientId, fetched.PatientId);
        Assert.Equal(created.CreatedAt, fetched.CreatedAt);
        Assert.Equal(AppointmentStatus.Pending, fetched.Status);
    }

    [Fact]
    public async Task Reschedule_IdMismatch_Returns400()
    {
        var created = await CreateAsync();
        var response = await _client.PutAsJsonAsync($"/api/appointments/{created.Id}",
            new UpdateAppointmentDto { Id = Guid.NewGuid(), BranchId = "b", AppointmentDate = Future() }, Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_NotFound_Returns404()
    {
        var id = Guid.NewGuid();
        var response = await _client.PutAsJsonAsync($"/api/appointments/{id}",
            new UpdateAppointmentDto { Id = id, BranchId = "b", AppointmentDate = Future() }, Json, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reschedule_PastDate_Returns400()
    {
        var created = await CreateAsync();
        var response = await _client.PutAsJsonAsync($"/api/appointments/{created.Id}",
            new UpdateAppointmentDto { Id = created.Id, BranchId = "b", AppointmentDate = DateTime.UtcNow.AddHours(-1) }, Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task Reschedule_FinalStatus_Returns409(AppointmentStatus status)
    {
        var created = await CreateInStatusAsync(status);
        var response = await _client.PutAsJsonAsync($"/api/appointments/{created.Id}",
            new UpdateAppointmentDto { Id = created.Id, BranchId = "b", AppointmentDate = Future(30) }, Json, Ct);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    public async Task Reschedule_OpenStatus_IsAllowed(AppointmentStatus status)
    {
        var created = await CreateInStatusAsync(status);
        var response = await _client.PutAsJsonAsync($"/api/appointments/{created.Id}",
            new UpdateAppointmentDto { Id = created.Id, BranchId = "b", AppointmentDate = Future(30) }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- Doktor slot çakışması ---

    private Task<HttpResponseMessage> PostAsync(CreateAppointmentDto dto)
        => _client.PostAsJsonAsync("/api/appointments", dto, Json, Ct);

    private Task<HttpResponseMessage> RescheduleRawAsync(Guid id, DateTime date)
        => _client.PutAsJsonAsync($"/api/appointments/{id}",
            new UpdateAppointmentDto { Id = id, BranchId = "branch-1", AppointmentDate = date }, Json, Ct);

    private static async Task AssertSlotConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        Assert.Equal("Bu doktorun bu saatte başka bir randevusu var.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Create_SameDoctorSameSlot_Returns409WithMessage()
    {
        var doctor = Unique("doc");
        await CreateAsync(NewAppointment(doctor, date: Slot(10)));
        await AssertSlotConflictAsync(await PostAsync(NewAppointment(doctor, date: Slot(10))));
    }

    [Fact]
    public async Task Create_DifferentDoctorSameSlot_Returns201()
    {
        await CreateAsync(NewAppointment(Unique("doc"), date: Slot(11)));
        await CreateAsync(NewAppointment(Unique("doc"), date: Slot(11)));
    }

    [Fact]
    public async Task Create_SameDoctorDifferentSlot_Returns201()
    {
        var doctor = Unique("doc");
        await CreateAsync(NewAppointment(doctor, date: Slot(12)));
        await CreateAsync(NewAppointment(doctor, date: Slot(13)));
    }

    [Fact]
    public async Task Create_SlotOfCancelledAppointment_CanBeTakenAgain()
    {
        var doctor = Unique("doc");
        await CreateInStatusAsync(AppointmentStatus.Cancelled, NewAppointment(doctor, date: Slot(14)));
        await CreateAsync(NewAppointment(doctor, date: Slot(14)));
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task Create_SlotHeldByActiveStatus_Returns409(AppointmentStatus status)
    {
        var doctor = Unique("doc");
        await CreateInStatusAsync(status, NewAppointment(doctor, date: Slot(15)));
        await AssertSlotConflictAsync(await PostAsync(NewAppointment(doctor, date: Slot(15))));
    }

    [Fact]
    public async Task Create_SecondsAreTruncated_SoTenOClockAndTenOClockThirtyCollide()
    {
        var doctor = Unique("doc");
        var first = await CreateAsync(NewAppointment(doctor, date: Slot(16)));
        Assert.Equal(Slot(16), first.AppointmentDate); // cevapta dakika hassasiyetli tarih

        await AssertSlotConflictAsync(await PostAsync(NewAppointment(doctor, date: Slot(16).AddSeconds(30).AddMilliseconds(123))));
    }

    [Fact]
    public async Task Reschedule_ToTakenSlot_Returns409()
    {
        var doctor = Unique("doc");
        await CreateAsync(NewAppointment(doctor, date: Slot(17)));
        var other = await CreateAsync(NewAppointment(doctor, date: Slot(18)));
        await AssertSlotConflictAsync(await RescheduleRawAsync(other.Id, Slot(17)));

        var fetched = await _client.GetFromJsonAsync<ResultAppointmentDto>($"/api/appointments/{other.Id}", Json, Ct);
        Assert.Equal(Slot(18), fetched!.AppointmentDate); // reddedilen taşıma tarihi değiştirmez
    }

    [Fact]
    public async Task Reschedule_ToOwnCurrentSlot_IsNotAFalseConflict()
    {
        var created = await CreateAsync(NewAppointment(Unique("doc"), date: Slot(19)));
        Assert.Equal(HttpStatusCode.OK, (await RescheduleRawAsync(created.Id, Slot(19))).StatusCode);
        // Aynı dakikanın içinde saniyeli tarih de kendi slotudur.
        Assert.Equal(HttpStatusCode.OK, (await RescheduleRawAsync(created.Id, Slot(19).AddSeconds(45))).StatusCode);
    }

    [Fact]
    public async Task Reschedule_ToSlotOfCancelledAppointment_IsAllowed()
    {
        var doctor = Unique("doc");
        await CreateInStatusAsync(AppointmentStatus.Cancelled, NewAppointment(doctor, date: Slot(20)));
        var other = await CreateAsync(NewAppointment(doctor, date: Slot(21)));
        Assert.Equal(HttpStatusCode.OK, (await RescheduleRawAsync(other.Id, Slot(20))).StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentRequestsForSameSlot_ExactlyOneWins()
    {
        var doctor = Unique("doc");
        const int parallel = 8;
        var responses = await Task.WhenAll(Enumerable.Range(0, parallel)
            .Select(_ => Task.Run(() => PostAsync(NewAppointment(doctor, date: Slot(22))), Ct)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(parallel - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.DoctorId == doctor, Ct));
    }

    [Fact]
    public async Task Database_RejectsSecondActiveAppointmentInSameSlot_ButAllowsCancelledOne()
    {
        var doctor = Unique("doc");
        await CreateAsync(NewAppointment(doctor, date: Slot(23)));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        Hastane.Appointment.Api.Entities.Appointment Make(AppointmentStatus status) => new()
        {
            Id = Guid.CreateVersion7(),
            DoctorId = doctor,
            PatientId = "p",
            BranchId = "b",
            AppointmentDate = Slot(23),
            Status = status,
            CreatedAt = DateTime.UtcNow,
        };

        db.Appointments.Add(Make(AppointmentStatus.Cancelled)); // filtre dışı: kabul
        await db.SaveChangesAsync(Ct);

        db.Appointments.Add(Make(AppointmentStatus.Confirmed)); // aktif: indeks reddeder
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    // --- Durum geçişleri ---

    public static TheoryData<AppointmentStatus, AppointmentStatus, bool> AllTransitions()
        => AppointmentStatusTransitionsTests.AllPairs();

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public async Task UpdateStatus_AllPairs_OkOrConflict(AppointmentStatus from, AppointmentStatus to, bool allowed)
    {
        var created = await CreateInStatusAsync(from);
        var response = await PatchStatusAsync(created.Id, to);

        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(to, (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!.Status);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var fetched = await _client.GetFromJsonAsync<ResultAppointmentDto>($"/api/appointments/{created.Id}", Json, Ct);
            Assert.Equal(from, fetched!.Status); // reddedilen geçiş durumu değiştirmez
        }
    }

    [Fact]
    public async Task UpdateStatus_NotFound_Returns404()
        => Assert.Equal(HttpStatusCode.NotFound, (await PatchStatusAsync(Guid.NewGuid(), AppointmentStatus.Confirmed)).StatusCode);

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"status":"Bogus"}""")]
    [InlineData("""{"status":99}""")]
    public async Task UpdateStatus_InvalidBody_Returns400(string body)
    {
        var created = await CreateAsync();
        var response = await _client.PatchAsync($"/api/appointments/{created.Id}/status",
            new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- Silme ve cascade ---

    [Fact]
    public async Task Delete_RemovesAppointment_ThenNotFound()
    {
        var created = await CreateAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/appointments/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/appointments/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/appointments/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Delete_CascadesToDetail()
    {
        var created = await CreateAsync();
        var put = await _client.PutAsJsonAsync($"/api/appointments/{created.Id}/detail",
            new UpsertAppointmentDetailDto { Complaint = "Öksürük" }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
            Assert.Equal(1, await db.AppointmentDetails.CountAsync(d => d.AppointmentId == created.Id, Ct));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/appointments/{created.Id}", Ct)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
            Assert.Equal(0, await db.AppointmentDetails.CountAsync(d => d.AppointmentId == created.Id, Ct));
        }
    }

    // --- İlişki (bire-bir) ---

    [Fact]
    public async Task Database_RejectsSecondDetailForSameAppointment()
    {
        var created = await CreateAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        db.AppointmentDetails.Add(new AppointmentDetail { Id = Guid.CreateVersion7(), AppointmentId = created.Id, Complaint = "bir" });
        await db.SaveChangesAsync(Ct);

        db.AppointmentDetails.Add(new AppointmentDetail { Id = Guid.CreateVersion7(), AppointmentId = created.Id, Complaint = "iki" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Database_RejectsDetailForMissingAppointment()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        db.AppointmentDetails.Add(new AppointmentDetail { Id = Guid.CreateVersion7(), AppointmentId = Guid.NewGuid(), Complaint = "yetim" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    // --- Detay ---

    [Fact]
    public async Task Detail_Get_NoDetail_Returns404()
    {
        var created = await CreateAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/appointments/{created.Id}/detail", Ct)).StatusCode);
    }

    [Fact]
    public async Task Detail_Upsert_CreatesThenUpdates_AndGetByIdIncludesIt()
    {
        var created = await CreateAsync();
        var url = $"/api/appointments/{created.Id}/detail";

        var first = await _client.PutAsJsonAsync(url, new UpsertAppointmentDetailDto { Complaint = "Baş ağrısı" }, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.NotNull(first.Headers.Location);
        var createdDetail = (await first.Content.ReadFromJsonAsync<AppointmentDetailDto>(Json, Ct))!;
        Assert.Equal(created.Id, createdDetail.AppointmentId);
        Assert.Equal("Baş ağrısı", createdDetail.Complaint);
        Assert.Null(createdDetail.UpdatedAt);

        var before = DateTime.UtcNow.AddSeconds(-1);
        var second = await _client.PutAsJsonAsync(url,
            new UpsertAppointmentDetailDto { Complaint = "Baş ağrısı", Diagnosis = "Migren", DoctorNotes = "Dinlenme" }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var updated = (await second.Content.ReadFromJsonAsync<AppointmentDetailDto>(Json, Ct))!;
        Assert.Equal(createdDetail.Id, updated.Id); // aynı satır güncellendi, ikincisi eklenmedi
        Assert.Equal("Migren", updated.Diagnosis);
        Assert.InRange(updated.UpdatedAt!.Value, before, DateTime.UtcNow.AddSeconds(1));

        var got = await _client.GetFromJsonAsync<AppointmentDetailDto>(url, Json, Ct);
        Assert.Equal("Dinlenme", got!.DoctorNotes);

        var fetched = await _client.GetFromJsonAsync<GetByIdAppointmentDto>($"/api/appointments/{created.Id}", Json, Ct);
        Assert.Equal("Migren", fetched!.Detail!.Diagnosis);
    }

    [Fact]
    public async Task Detail_Upsert_AppointmentMissing_Returns404()
    {
        var response = await _client.PutAsJsonAsync($"/api/appointments/{Guid.NewGuid()}/detail",
            new UpsertAppointmentDetailDto { Complaint = "x" }, Json, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_Upsert_InvalidBody_Returns400()
    {
        var created = await CreateAsync();
        var url = $"/api/appointments/{created.Id}/detail";
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PutAsJsonAsync(url, new UpsertAppointmentDetailDto { Complaint = "" }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PutAsJsonAsync(url, new UpsertAppointmentDetailDto { Complaint = new string('a', 1001) }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PutAsJsonAsync(url, new UpsertAppointmentDetailDto { Complaint = "x", Diagnosis = new string('a', 1001) }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.PutAsJsonAsync(url, new UpsertAppointmentDetailDto { Complaint = "x", DoctorNotes = new string('a', 2001) }, Json, Ct)).StatusCode);
        // Sınır değerler kabul edilir.
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PutAsJsonAsync(url, new UpsertAppointmentDetailDto
            {
                Complaint = new string('a', 1000),
                Diagnosis = new string('b', 1000),
                DoctorNotes = new string('c', 2000),
            }, Json, Ct)).StatusCode);
    }

    // --- Bozuk Guid ---

    [Fact]
    public async Task MalformedGuid_Returns404_OnEveryIdRoute()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/appointments/not-a-guid", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/appointments/not-a-guid", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/appointments/not-a-guid/detail", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PutAsJsonAsync("/api/appointments/not-a-guid/detail", new UpsertAppointmentDetailDto { Complaint = "x" }, Json, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PatchAsync("/api/appointments/not-a-guid/status",
                new StringContent("""{"status":"Confirmed"}""", Encoding.UTF8, "application/json"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PutAsync("/api/appointments/not-a-guid",
                new StringContent("{}", Encoding.UTF8, "application/json"), Ct)).StatusCode);
    }

    [Fact]
    public async Task UnknownId_Returns404()
    {
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/appointments/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/appointments/{id}/detail", Ct)).StatusCode);
    }
}
