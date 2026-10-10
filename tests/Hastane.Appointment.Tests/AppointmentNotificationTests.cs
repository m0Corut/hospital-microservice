using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Appointment.Tests;

// Randevu -> Notification: bildirim kayıttan SONRA gider, alıcı/bağlantı doğru olur, iç anahtar başlığı eklenir,
// Notification kapalı/500/yavaş olsa da randevu işlemi başarılı kalır. Notification sahte (FakeNotificationService).
public class AppointmentNotificationTests : IClassFixture<AppointmentApiFactory>, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppointmentApiFactory _factory;

    public AppointmentNotificationTests(AppointmentApiFactory factory)
    {
        _factory = factory;
        _factory.Notification.Reset();
    }

    public void Dispose() => _factory.Notification.Reset();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private HttpClient Client(string token) => _factory.CreateClient().WithBearer(token);

    private static string PatientToken(string sub) => TestJwt.Create(sub, [HastaneRoles.Patient]);
    private static string DoctorToken(string doctorId) => TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], doctorId);
    private static string AdminToken() => TestJwt.Create(Unique("adm"), [HastaneRoles.Admin]);

    // Her test kendi gelecek dakikasını alır: aynı doktor+saat çakışması olmasın diye doktor da benzersiz.
    private static DateTime Slot(int days = 20)
    {
        var t = DateTime.UtcNow.AddDays(days);
        return new DateTime(t.Ticks - t.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    }

    private async Task<HttpResponseMessage> PostAsync(string patient, string doctorId, DateTime? date = null)
        => await Client(PatientToken(patient)).PostAsJsonAsync("/api/appointments", new CreateAppointmentDto
        {
            DoctorId = doctorId,
            BranchId = "branch-1",
            AppointmentDate = date ?? Slot(),
        }, Json, Ct);

    private async Task<ResultAppointmentDto> CreateAsync(string patient, string doctorId)
    {
        var response = await PostAsync(patient, doctorId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;
    }

    // ---- oluşturma ----

    [Fact]
    public async Task Create_NotifiesPatientAndDoctor_WithRecipientAndLink()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");

        var created = await CreateAsync(patient, doctorId);

        var calls = _factory.Notification.CallsFor(created.Id);
        Assert.Equal(2, calls.Count);

        var toPatient = Assert.Single(calls, c => c.UserId == patient);
        Assert.Null(toPatient.DoctorId);
        Assert.Equal($"/Patient/Appointments/Details/{created.Id}", toPatient.Link);
        Assert.StartsWith("Randevunuz alındı:", toPatient.Message);

        var toDoctor = Assert.Single(calls, c => c.DoctorId == doctorId);
        Assert.Null(toDoctor.UserId);
        Assert.Equal($"/Doctor/Appointments/Details/{created.Id}", toDoctor.Link);
        Assert.Equal("Yeni randevu talebi", toDoctor.Title);
        Assert.All(calls, c => Assert.Equal("/api/internal/notifications", c.Path));
    }

    [Fact]
    public async Task Create_SendsInternalKeyHeader()
    {
        var created = await CreateAsync(Unique("pat"), Unique("doc"));

        Assert.All(_factory.Notification.CallsFor(created.Id),
            c => Assert.Equal(AppointmentApiFactory.TestInternalKey, c.Key));
    }

    [Fact]
    public async Task Create_WhenRejected_ConflictOrInvalid_SendsNoNotification()
    {
        var doctorId = Unique("doc");
        var date = Slot(21);
        await CreateAsync(Unique("pat"), doctorId); // farklı slot: bu satır yalnız doktoru kullanılmış yapar
        var first = await PostAsync(Unique("pat"), doctorId, date);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var before = _factory.Notification.Calls.Count;

        var second = await PostAsync(Unique("pat"), doctorId, date); // aynı doktor+saat

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(before, _factory.Notification.Calls.Count);
    }

    // ---- Notification çalışmıyor: randevu yine başarılı ----

    [Theory]
    [InlineData(NotificationMode.Down)]
    [InlineData(NotificationMode.Error500)]
    [InlineData(NotificationMode.Unauthorized)]
    [InlineData(NotificationMode.Timeout)]
    public async Task Create_WhenNotificationFails_StillReturns201_AndAppointmentIsSaved(NotificationMode mode)
    {
        _factory.Notification.Mode = mode;
        var patient = Unique("pat");

        var response = await PostAsync(patient, Unique("doc"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;
        var fetched = await Client(PatientToken(patient)).GetAsync($"/api/appointments/{created.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.NotEmpty(_factory.Notification.CallsFor(created.Id)); // gönderim denendi
    }

    [Theory]
    [InlineData(NotificationMode.Down)]
    [InlineData(NotificationMode.Error500)]
    [InlineData(NotificationMode.Timeout)]
    public async Task StatusAndReschedule_WhenNotificationFails_StillSucceed(NotificationMode mode)
    {
        var doctorId = Unique("doc");
        var created = await CreateAsync(Unique("pat"), doctorId);
        _factory.Notification.Mode = mode;
        var doctor = Client(DoctorToken(doctorId));

        var reschedule = await doctor.PutAsJsonAsync($"/api/appointments/{created.Id}",
            new UpdateAppointmentDto { Id = created.Id, BranchId = "branch-2", AppointmentDate = Slot(25) }, Json, Ct);
        var confirm = await doctor.PatchAsJsonAsync($"/api/appointments/{created.Id}/status",
            new UpdateStatusDto { Status = AppointmentStatus.Confirmed }, Json, Ct);

        Assert.Equal(HttpStatusCode.OK, reschedule.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
    }

    // ---- durum değişimi: karşı taraf ----

    [Fact]
    public async Task Status_ChangedByDoctor_NotifiesPatient()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");
        var created = await CreateAsync(patient, doctorId);
        var before = _factory.Notification.CallsFor(created.Id).Count;

        var response = await Client(DoctorToken(doctorId)).PatchAsJsonAsync($"/api/appointments/{created.Id}/status",
            new UpdateStatusDto { Status = AppointmentStatus.Confirmed }, Json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var added = _factory.Notification.CallsFor(created.Id).Skip(before).ToList();
        var call = Assert.Single(added);
        Assert.Equal(patient, call.UserId);
        Assert.Null(call.DoctorId);
        Assert.Equal($"/Patient/Appointments/Details/{created.Id}", call.Link);
        Assert.Contains("onaylandı", call.Message);
    }

    [Fact]
    public async Task Status_ChangedByPatient_NotifiesDoctor()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");
        var created = await CreateAsync(patient, doctorId);
        var before = _factory.Notification.CallsFor(created.Id).Count;

        var response = await Client(PatientToken(patient)).PatchAsJsonAsync($"/api/appointments/{created.Id}/status",
            new UpdateStatusDto { Status = AppointmentStatus.Cancelled }, Json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_factory.Notification.CallsFor(created.Id).Skip(before));
        Assert.Equal(doctorId, call.DoctorId);
        Assert.Null(call.UserId);
        Assert.Equal($"/Doctor/Appointments/Details/{created.Id}", call.Link);
    }

    [Fact]
    public async Task Status_ChangedByAdmin_NotifiesPatient()
    {
        var patient = Unique("pat");
        var created = await CreateAsync(patient, Unique("doc"));
        var before = _factory.Notification.CallsFor(created.Id).Count;

        var response = await Client(AdminToken()).PatchAsJsonAsync($"/api/appointments/{created.Id}/status",
            new UpdateStatusDto { Status = AppointmentStatus.Cancelled }, Json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(_factory.Notification.CallsFor(created.Id).Skip(before));
        Assert.Equal(patient, call.UserId);
        Assert.Contains("iptal edildi", call.Message);
    }

    [Fact]
    public async Task Status_InvalidTransition_SendsNoNotification()
    {
        var doctorId = Unique("doc");
        var created = await CreateAsync(Unique("pat"), doctorId);
        var before = _factory.Notification.CallsFor(created.Id).Count;

        // Pending -> Completed geçersiz (409)
        var response = await Client(DoctorToken(doctorId)).PatchAsJsonAsync($"/api/appointments/{created.Id}/status",
            new UpdateStatusDto { Status = AppointmentStatus.Completed }, Json, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, _factory.Notification.CallsFor(created.Id).Count);
    }

    // ---- yeniden planlama: ikisine ----

    [Fact]
    public async Task Reschedule_NotifiesBoth()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");
        var created = await CreateAsync(patient, doctorId);
        var before = _factory.Notification.CallsFor(created.Id).Count;

        var response = await Client(PatientToken(patient)).PutAsJsonAsync($"/api/appointments/{created.Id}",
            new UpdateAppointmentDto { Id = created.Id, BranchId = "branch-2", AppointmentDate = Slot(30) }, Json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var added = _factory.Notification.CallsFor(created.Id).Skip(before).ToList();
        Assert.Equal(2, added.Count);
        Assert.Contains(added, c => c.UserId == patient && c.Link == $"/Patient/Appointments/Details/{created.Id}");
        Assert.Contains(added, c => c.DoctorId == doctorId && c.Link == $"/Doctor/Appointments/Details/{created.Id}");
        Assert.All(added, c => Assert.Equal(AppointmentApiFactory.TestInternalKey, c.Key));
    }
}
