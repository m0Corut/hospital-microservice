using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Appointment.Tests;

// Boş saat hesabı (availability), randevuda program kontrolü ve randevu notu.
// Her test kendi doktorunu Register eder; Doctor servisi sahte (FakeDoctorService), veritabanı gerçek SQL Server.
public class AvailabilityAndScheduleTests : IClassFixture<AppointmentApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private readonly AppointmentApiFactory _factory;

    public AvailabilityAndScheduleTests(AppointmentApiFactory factory) => _factory = factory;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _factory.Doctor.Reset();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static string AdminToken() => TestJwt.Create(Unique("adm"), [HastaneRoles.Admin]);
    private static string PatientToken(string? sub = null) => TestJwt.Create(sub ?? Unique("pat"), [HastaneRoles.Patient]);

    private HttpClient Client(string? token = null)
    {
        var client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    // Verilen gün adından en az 30 gün sonraki ilk tarih (yerel/İstanbul takvim günü).
    private static DateOnly Next(DayOfWeek day)
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        while (d.DayOfWeek != day) d = d.AddDays(1);
        return d;
    }

    // İstanbul yerel saati -> UTC (test beklentisini sabit +3 yazmak yerine saat diliminden hesaplar).
    private static DateTime LocalToUtc(DateOnly date, string hhmm)
        => TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.Parse(hhmm), DateTimeKind.Unspecified), Istanbul);

    private static string Doc(string prefix = "doc") => Unique(prefix);

    // Pazartesi: Ankara 09:00-11:00 (30 dk) + İzmir 14:00-15:00 (30 dk). Salı: Ankara 09:00-10:00 (60 dk).
    private string RegisterTypicalDoctor()
    {
        var id = Doc();
        _factory.Doctor.Register(id,
            new FakeClinic("Ankara Şehir", "Ankara", new FakeEntry(DayOfWeek.Monday, "09:00", "11:00", 30)),
            new FakeClinic("İzmir Özel", "İzmir",
                new FakeEntry(DayOfWeek.Monday, "14:00", "15:00", 30),
                new FakeEntry(DayOfWeek.Tuesday, "09:00", "10:00", 60)));
        return id;
    }

    private async Task<List<AvailableSlotDto>> AvailabilityAsync(string doctorId, DateOnly date, string? token = null)
    {
        var response = await Client(token ?? PatientToken()).GetAsync($"/api/appointments/availability?doctorId={doctorId}&date={date:yyyy-MM-dd}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<AvailableSlotDto>>(Json, Ct))!;
    }

    private Task<HttpResponseMessage> PostAsync(string doctorId, DateTime utc, string? note = null, string? token = null, string? patientId = null)
        => Client(token ?? PatientToken()).PostAsJsonAsync("/api/appointments", new CreateAppointmentDto
        {
            DoctorId = doctorId,
            PatientId = patientId,
            BranchId = "branch-1",
            AppointmentDate = utc,
            Note = note,
        }, Json, Ct);

    // --- availability: hesap ---

    [Fact]
    public async Task Availability_ReturnsScheduleSlots_InUtc_SortedWithClinicInfo()
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);

        var slots = await AvailabilityAsync(doctorId, monday);

        // İstanbul 09:00 = 06:00Z. Ankara 4 slot, İzmir 2 slot, başlangıca göre sıralı.
        Assert.Equal(
            new[] { "09:00", "09:30", "10:00", "10:30", "14:00", "14:30" }.Select(t => LocalToUtc(monday, t)),
            slots.Select(s => s.Start));
        Assert.Equal(new DateTime(monday.Year, monday.Month, monday.Day, 6, 0, 0, DateTimeKind.Utc), slots[0].Start);
        Assert.All(slots, s => Assert.Equal(s.Start.AddMinutes(30), s.End));
        Assert.Equal(["Ankara Şehir", "Ankara Şehir", "Ankara Şehir", "Ankara Şehir", "İzmir Özel", "İzmir Özel"], slots.Select(s => s.ClinicName));
        Assert.Equal(["Ankara", "Ankara", "Ankara", "Ankara", "İzmir", "İzmir"], slots.Select(s => s.City));
    }

    [Fact]
    public async Task Availability_JsonStartIsUtcWithZ_AndCamelCase()
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);

        var raw = await Client(PatientToken()).GetStringAsync($"/api/appointments/availability?doctorId={doctorId}&date={monday:yyyy-MM-dd}", Ct);

        using var doc = JsonDocument.Parse(raw);
        var first = doc.RootElement[0];
        Assert.EndsWith("Z", first.GetProperty("start").GetString());
        Assert.True(first.TryGetProperty("end", out _));
        Assert.True(first.TryGetProperty("clinicName", out _));
        Assert.True(first.TryGetProperty("city", out _));
    }

    [Fact]
    public async Task Availability_DayWithoutSchedule_IsEmpty()
    {
        var doctorId = RegisterTypicalDoctor();

        Assert.Empty(await AvailabilityAsync(doctorId, Next(DayOfWeek.Wednesday)));
        // Salı yalnız İzmir'de tek slot.
        var tuesday = await AvailabilityAsync(doctorId, Next(DayOfWeek.Tuesday));
        Assert.Equal("İzmir Özel", Assert.Single(tuesday).ClinicName);
    }

    [Fact]
    public async Task Availability_RemovesBookedSlots_OfThatDoctorOnly()
    {
        var doctorId = RegisterTypicalDoctor();
        var otherDoctor = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);
        var booked = LocalToUtc(monday, "09:30");

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(doctorId, booked)).StatusCode);

        var slots = await AvailabilityAsync(doctorId, monday);
        Assert.DoesNotContain(slots, s => s.Start == booked);
        Assert.Equal(5, slots.Count);
        // Başka doktorun aynı saati hâlâ boş.
        Assert.Contains(await AvailabilityAsync(otherDoctor, monday), s => s.Start == booked);
    }

    [Fact]
    public async Task Availability_CancelledAppointment_FreesTheSlot()
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);
        var slot = LocalToUtc(monday, "10:00");
        var response = await PostAsync(doctorId, slot);
        var created = (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;
        Assert.DoesNotContain(await AvailabilityAsync(doctorId, monday), s => s.Start == slot);

        var cancel = await Client(AdminToken()).PatchAsJsonAsync(
            $"/api/appointments/{created.Id}/status", new UpdateStatusDto { Status = AppointmentStatus.Cancelled }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        Assert.Contains(await AvailabilityAsync(doctorId, monday), s => s.Start == slot);
    }

    [Fact]
    public async Task Availability_PastHoursAreExcluded()
    {
        var doctorId = Doc();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Istanbul));
        _factory.Doctor.Register(doctorId,
            new FakeClinic("Gün Boyu", "Ankara", new FakeEntry(today.DayOfWeek, "00:00", "24:00", 60)));

        var before = DateTime.UtcNow;
        var slots = await AvailabilityAsync(doctorId, today);

        Assert.All(slots, s => Assert.True(s.Start > before, $"{s.Start:O} geçmişte kaldı"));
        var hours = Enumerable.Range(0, 24).Select(h => LocalToUtc(today, $"{h:00}:00")).ToList();
        Assert.InRange(slots.Count, hours.Count(h => h > DateTime.UtcNow), hours.Count(h => h > before));

        // Dün tümüyle geçmişte.
        var yesterday = today.AddDays(-1);
        _factory.Doctor.Register(doctorId,
            new FakeClinic("Gün Boyu", "Ankara", new FakeEntry(yesterday.DayOfWeek, "00:00", "24:00", 60)));
        Assert.Empty(await AvailabilityAsync(doctorId, yesterday));
    }

    // --- availability: sözleşme, yetki, Doctor hataları ---

    [Fact]
    public async Task Availability_Anonymous_Returns401()
    {
        var response = await Client().GetAsync("/api/appointments/availability?doctorId=x&date=2030-01-07", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Patient")]
    [InlineData("Doctor")]
    public async Task Availability_AnyLoggedInRole_Returns200(string role)
    {
        var doctorId = RegisterTypicalDoctor();
        var token = TestJwt.Create(Unique("usr"), [role], role == "Doctor" ? Doc() : null);

        var slots = await AvailabilityAsync(doctorId, Next(DayOfWeek.Monday), token);

        Assert.NotEmpty(slots);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?doctorId=abc")]
    [InlineData("?date=2030-01-07")]
    [InlineData("?doctorId=abc&date=yarin")]
    [InlineData("?doctorId=abc&date=2030-13-45")]
    public async Task Availability_MissingOrBadParameters_Returns400(string query)
    {
        var response = await Client(PatientToken()).GetAsync("/api/appointments/availability" + query, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Availability_ForwardsAuthorizationHeader_ToDoctorService()
    {
        var doctorId = RegisterTypicalDoctor();
        var token = PatientToken();

        await AvailabilityAsync(doctorId, Next(DayOfWeek.Monday), token);

        var call = Assert.Single(_factory.Doctor.Calls, c => c.Path == $"/api/doctors/{doctorId}");
        Assert.Equal("Bearer " + token, call.Authorization);
    }

    [Fact]
    public async Task Availability_UnknownDoctor_Returns404()
    {
        var doctorId = Doc();
        _factory.Doctor.Register(doctorId, null);

        var response = await Client(PatientToken()).GetAsync($"/api/appointments/availability?doctorId={doctorId}&date=2030-01-07", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(DoctorMode.Down)]
    [InlineData(DoctorMode.Timeout)]
    [InlineData(DoctorMode.Error500)]
    public async Task Availability_DoctorServiceFailing_Returns503(DoctorMode mode)
    {
        _factory.Doctor.Mode = mode;

        var response = await Client(PatientToken()).GetAsync($"/api/appointments/availability?doctorId={Doc()}&date=2030-01-07", Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // --- POST: program kontrolü ---

    [Fact]
    public async Task Create_SlotStartInsideSchedule_Returns201()
    {
        var doctorId = RegisterTypicalDoctor();

        var response = await PostAsync(doctorId, LocalToUtc(Next(DayOfWeek.Monday), "14:30"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("09:15")]   // slot başlangıcı değil
    [InlineData("11:00")]   // aralığın bitişi (son slot 10:30)
    [InlineData("08:30")]   // mesai öncesi
    [InlineData("12:00")]   // iki klinik arası boşluk
    public async Task Create_TimeNotOnScheduleSlot_Returns400(string localTime)
    {
        var doctorId = RegisterTypicalDoctor();

        var response = await PostAsync(doctorId, LocalToUtc(Next(DayOfWeek.Monday), localTime));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Seçilen saat doktorun çalışma programında değil", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Create_DayWithoutSchedule_Returns400()
    {
        var doctorId = RegisterTypicalDoctor();

        var response = await PostAsync(doctorId, LocalToUtc(Next(DayOfWeek.Thursday), "09:00"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ScheduleIsReadInClinicLocalTime_NotUtc()
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);

        // 09:00 UTC = İstanbul 12:00: programda yok. 06:00 UTC = İstanbul 09:00: var.
        var utc0900 = new DateTime(monday.Year, monday.Month, monday.Day, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(doctorId, utc0900)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(doctorId, utc0900.AddHours(-3))).StatusCode);
    }

    [Fact]
    public async Task Create_AdminFollowsTheSameScheduleRule()
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);

        var bad = await PostAsync(doctorId, LocalToUtc(monday, "09:15"), token: AdminToken(), patientId: "p-1");
        var good = await PostAsync(doctorId, LocalToUtc(monday, "09:00"), token: AdminToken(), patientId: "p-1");

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.Created, good.StatusCode);
    }

    [Fact]
    public async Task Create_SecondsAreIgnored_ExactSlotMinuteStillMatches()
    {
        var doctorId = RegisterTypicalDoctor();

        // Mevcut dakika normalizasyonu: 09:00:30 => 09:00 slotu.
        var response = await PostAsync(doctorId, LocalToUtc(Next(DayOfWeek.Monday), "09:00").AddSeconds(30));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownDoctor_Returns400_AndNothingIsCreated()
    {
        var doctorId = Doc();
        _factory.Doctor.Register(doctorId, null);

        var response = await PostAsync(doctorId, LocalToUtc(Next(DayOfWeek.Monday), "09:00"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(DoctorMode.Down)]
    [InlineData(DoctorMode.Timeout)]
    [InlineData(DoctorMode.Error500)]
    public async Task Create_DoctorServiceFailing_Returns503_AndNoAppointmentIsCreated(DoctorMode mode)
    {
        var doctorId = Doc();
        _factory.Doctor.Mode = mode;

        var response = await PostAsync(doctorId, DateTime.UtcNow.AddDays(10));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        _factory.Doctor.Reset();
        var list = await Client(AdminToken()).GetFromJsonAsync<List<ResultAppointmentDto>>($"/api/appointments?doctorId={doctorId}", Json, Ct);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Create_ForwardsAuthorizationHeader_ToDoctorService()
    {
        var doctorId = RegisterTypicalDoctor();
        var token = PatientToken();

        await PostAsync(doctorId, LocalToUtc(Next(DayOfWeek.Monday), "09:00"), token: token);

        var call = Assert.Single(_factory.Doctor.Calls, c => c.Path == $"/api/doctors/{doctorId}");
        Assert.Equal("Bearer " + token, call.Authorization);
    }

    [Fact]
    public async Task Create_SameSlotTwice_SecondIs409_AndTakenSlotLeavesAvailability()
    {
        var doctorId = RegisterTypicalDoctor();
        var slot = LocalToUtc(Next(DayOfWeek.Monday), "10:30");

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(doctorId, slot)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(doctorId, slot)).StatusCode);
    }

    [Fact]
    public async Task Create_ConcurrentRequestsForSameScheduledSlot_ExactlyOneWins()
    {
        var doctorId = RegisterTypicalDoctor();
        var slot = LocalToUtc(Next(DayOfWeek.Monday), "09:00");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostAsync(doctorId, slot)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    // --- Randevu notu ---

    [Fact]
    public async Task Create_NoteIsReturned_InCreateAndGetById()
    {
        var doctorId = RegisterTypicalDoctor();
        var patient = Unique("pat");
        var client = Client(PatientToken(patient));
        var body = new CreateAppointmentDto
        {
            DoctorId = doctorId,
            BranchId = "branch-1",
            AppointmentDate = LocalToUtc(Next(DayOfWeek.Monday), "09:00"),
            Note = "  Üç gündür baş ağrım var.  ",
        };

        var response = await client.PostAsJsonAsync("/api/appointments", body, Json, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;
        Assert.Equal("Üç gündür baş ağrım var.", created.Note);
        var fetched = (await client.GetFromJsonAsync<GetByIdAppointmentDto>($"/api/appointments/{created.Id}", Json, Ct))!;
        Assert.Equal("Üç gündür baş ağrım var.", fetched.Note);
    }

    [Fact]
    public async Task Create_NoteIsOptional_NullOrBlankBecomesNull()
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);

        var noNote = (await (await PostAsync(doctorId, LocalToUtc(monday, "09:00"))).Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;
        var blank = (await (await PostAsync(doctorId, LocalToUtc(monday, "09:30"), note: "   ")).Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;

        Assert.Null(noNote.Note);
        Assert.Null(blank.Note);
    }

    [Fact]
    public async Task Create_NoteLengthLimit_500Ok_501Rejected()
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);

        var ok = await PostAsync(doctorId, LocalToUtc(monday, "09:00"), note: new string('a', 500));
        var tooLong = await PostAsync(doctorId, LocalToUtc(monday, "09:30"), note: new string('a', 501));

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Reschedule_DoesNotChangeTheNote()
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);
        var admin = Client(AdminToken());
        var created = (await (await PostAsync(doctorId, LocalToUtc(monday, "09:00"), note: "ilk not")).Content
            .ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;

        var put = await admin.PutAsJsonAsync($"/api/appointments/{created.Id}",
            new UpdateAppointmentDto { Id = created.Id, BranchId = "branch-2", AppointmentDate = LocalToUtc(monday, "10:00") }, Json, Ct);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("ilk not", (await put.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!.Note);
    }

    // --- PUT (yeniden planlama): POST ile aynı program kuralı ---

    private async Task<ResultAppointmentDto> CreateAtAsync(string doctorId, DateTime utc)
        => (await (await PostAsync(doctorId, utc)).Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;

    private Task<HttpResponseMessage> RescheduleAsync(Guid id, DateTime utc)
        => Client(AdminToken()).PutAsJsonAsync($"/api/appointments/{id}",
            new UpdateAppointmentDto { Id = id, BranchId = "branch-1", AppointmentDate = utc }, Json, Ct);

    private async Task<DateTime> CurrentDateAsync(Guid id)
        => (await Client(AdminToken()).GetFromJsonAsync<GetByIdAppointmentDto>($"/api/appointments/{id}", Json, Ct))!.AppointmentDate;

    [Fact]
    public async Task Reschedule_ToSlotInsideSchedule_Returns200()
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);
        var created = await CreateAtAsync(doctorId, LocalToUtc(monday, "09:00"));

        var response = await RescheduleAsync(created.Id, LocalToUtc(monday, "14:30"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LocalToUtc(monday, "14:30"), await CurrentDateAsync(created.Id));
    }

    [Theory]
    [InlineData("09:15")]
    [InlineData("12:00")]
    public async Task Reschedule_ToTimeOutsideSchedule_Returns400_AndDateUnchanged(string localTime)
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);
        var original = LocalToUtc(monday, "09:00");
        var created = await CreateAtAsync(doctorId, original);

        var response = await RescheduleAsync(created.Id, LocalToUtc(monday, localTime));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Seçilen saat doktorun çalışma programında değil", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(original, await CurrentDateAsync(created.Id));
    }

    [Fact]
    public async Task Reschedule_ToTakenScheduledSlot_StillReturns409()
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);
        await CreateAtAsync(doctorId, LocalToUtc(monday, "09:00"));
        var second = await CreateAtAsync(doctorId, LocalToUtc(monday, "09:30"));

        var response = await RescheduleAsync(second.Id, LocalToUtc(monday, "09:00"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(DoctorMode.Down)]
    [InlineData(DoctorMode.Timeout)]
    [InlineData(DoctorMode.Error500)]
    public async Task Reschedule_DoctorServiceFailing_Returns503_AndDateUnchanged(DoctorMode mode)
    {
        var doctorId = RegisterTypicalDoctor();
        var monday = Next(DayOfWeek.Monday);
        var original = LocalToUtc(monday, "09:00");
        var created = await CreateAtAsync(doctorId, original);
        _factory.Doctor.Mode = mode;

        var response = await RescheduleAsync(created.Id, LocalToUtc(monday, "10:00"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        _factory.Doctor.Reset();
        Assert.Equal(original, await CurrentDateAsync(created.Id));
    }
}
