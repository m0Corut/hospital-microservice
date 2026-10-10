using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Appointment.Api.Data;
using Hastane.Appointment.Api.Dtos;
using Hastane.Appointment.Api.Entities;
using Hastane.Contracts;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Appointment.Tests;

// Randevu -> olay yayını (RabbitMQ yerine MassTransit test harness; olay yine EF outbox üzerinden geçer).
// Eski senkron bildirim testlerinin karşılığı: "kime hangi bildirim" kararı artık Notification tüketicisinde (orada test edilir);
// burada yayıncının sorumluluğu sınanır: doğru olay, doğru alanlar, doğru rol, reddedilen işlemde olay YOK, RabbitMQ yokken de 201.
public class AppointmentEventTests : IClassFixture<AppointmentApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly AppointmentApiFactory _factory;

    public AppointmentEventTests(AppointmentApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
    private ITestHarness Harness => _factory.Services.GetTestHarness();

    private HttpClient Client(string token) => _factory.CreateClient().WithBearer(token);

    private static string PatientToken(string sub) => TestJwt.Create(sub, [HastaneRoles.Patient]);
    private static string DoctorToken(string doctorId) => TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], doctorId);
    private static string AdminToken() => TestJwt.Create(Unique("adm"), [HastaneRoles.Admin]);

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

    private async Task<ResultAppointmentDto> CreateAsync(string patient, string doctorId, DateTime? date = null)
    {
        var response = await PostAsync(patient, doctorId, date);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;
    }

    private async Task<AppointmentStatusChanged> ChangeStatusAsync(
        HttpClient client, ResultAppointmentDto appointment, AppointmentStatus status)
    {
        var response = await client.PatchAsJsonAsync($"/api/appointments/{appointment.Id}/status",
            new UpdateStatusDto { Status = status }, Json, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var published = await Harness.Published.SelectAsync<AppointmentStatusChanged>(
            m => m.Context.Message.AppointmentId == appointment.Id && m.Context.Message.NewStatus == status.ToString(), Ct).FirstOrDefault();
        Assert.NotNull(published);
        return published.Context.Message;
    }

    // ---- oluşturma ----

    [Fact]
    public async Task Create_PublishesAppointmentCreated_WithMinimalFields()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");

        var created = await CreateAsync(patient, doctorId);

        var published = await Harness.Published.SelectAsync<AppointmentCreated>(m => m.Context.Message.AppointmentId == created.Id, Ct).FirstOrDefault();
        Assert.NotNull(published);
        var e = published.Context.Message;
        Assert.NotEqual(Guid.Empty, e.EventId);
        Assert.Equal(patient, e.PatientUserId);
        Assert.Equal(doctorId, e.DoctorId);
        Assert.Equal(created.AppointmentDate, e.AppointmentDate);
    }

    [Fact]
    public async Task Create_TwoAppointments_PublishDistinctEventIds()
    {
        var a = await CreateAsync(Unique("pat"), Unique("doc"));
        var b = await CreateAsync(Unique("pat"), Unique("doc"));

        var ea = await Harness.Published.SelectAsync<AppointmentCreated>(m => m.Context.Message.AppointmentId == a.Id, Ct).FirstOrDefault();
        var eb = await Harness.Published.SelectAsync<AppointmentCreated>(m => m.Context.Message.AppointmentId == b.Id, Ct).FirstOrDefault();
        Assert.NotNull(ea);
        Assert.NotNull(eb);
        Assert.NotEqual(ea.Context.Message.EventId, eb.Context.Message.EventId);
    }

    [Fact]
    public async Task Create_WhenSlotConflict_PublishesNoSecondEvent()
    {
        var doctorId = Unique("doc");
        var date = Slot(21);
        var first = await CreateAsync(Unique("pat"), doctorId, date);

        var second = await PostAsync(Unique("pat"), doctorId, date); // aynı doktor+saat

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        // Reddedilen istek outbox satırı da bırakmaz (aynı transaction geri alındı): bu doktor için tek olay.
        await Harness.Published.Any<AppointmentCreated>(m => m.Context.Message.AppointmentId == first.Id, Ct);
        var forDoctor = Harness.Published.Select<AppointmentCreated>(m => m.Context.Message.DoctorId == doctorId, Ct).ToList();
        Assert.Single(forDoctor);
    }

    // ---- durum değişimi ----

    [Fact]
    public async Task Status_ChangedByDoctor_PublishesWithDoctorRole_AndOldAndNewStatus()
    {
        var patient = Unique("pat");
        var doctorId = Unique("doc");
        var created = await CreateAsync(patient, doctorId);

        var e = await ChangeStatusAsync(Client(DoctorToken(doctorId)), created, AppointmentStatus.Confirmed);

        Assert.Equal("Doctor", e.ChangedByRole);
        Assert.Equal("Pending", e.OldStatus);
        Assert.Equal("Confirmed", e.NewStatus);
        Assert.Equal(patient, e.PatientUserId);
        Assert.Equal(doctorId, e.DoctorId);
    }

    [Fact]
    public async Task Status_ChangedByPatient_PublishesWithPatientRole()
    {
        var patient = Unique("pat");
        var created = await CreateAsync(patient, Unique("doc"));

        var e = await ChangeStatusAsync(Client(PatientToken(patient)), created, AppointmentStatus.Cancelled);

        Assert.Equal("Patient", e.ChangedByRole);
        Assert.Equal("Cancelled", e.NewStatus);
    }

    [Fact]
    public async Task Status_ChangedByAdmin_PublishesWithAdminRole()
    {
        var created = await CreateAsync(Unique("pat"), Unique("doc"));

        var e = await ChangeStatusAsync(Client(AdminToken()), created, AppointmentStatus.Cancelled);

        Assert.Equal("Admin", e.ChangedByRole);
    }

    [Fact]
    public async Task Status_InvalidTransition_PublishesNoStatusEvent()
    {
        var doctorId = Unique("doc");
        var created = await CreateAsync(Unique("pat"), doctorId);

        // Pending -> Completed geçersiz (409)
        var response = await Client(DoctorToken(doctorId)).PatchAsJsonAsync($"/api/appointments/{created.Id}/status",
            new UpdateStatusDto { Status = AppointmentStatus.Completed }, Json, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(await Harness.Published.Any<AppointmentStatusChanged>(m => m.Context.Message.AppointmentId == created.Id, Ct));
    }

    // ---- yeniden planlama ----

    [Fact]
    public async Task Reschedule_PublishesAppointmentRescheduled_WithOldAndNewDate()
    {
        var patient = Unique("pat");
        var created = await CreateAsync(patient, Unique("doc"));
        var newDate = Slot(30);

        var response = await Client(PatientToken(patient)).PutAsJsonAsync($"/api/appointments/{created.Id}",
            new UpdateAppointmentDto { Id = created.Id, BranchId = "branch-2", AppointmentDate = newDate }, Json, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var published = await Harness.Published.SelectAsync<AppointmentRescheduled>(m => m.Context.Message.AppointmentId == created.Id, Ct).FirstOrDefault();
        Assert.NotNull(published);
        var e = published.Context.Message;
        Assert.Equal(created.AppointmentDate, e.OldDate);
        Assert.Equal(newDate, e.NewDate);
        Assert.Equal(newDate, e.AppointmentDate);
    }

    // ---- outbox: tablolar migration ile kurulur ----

    [Fact]
    public async Task OutboxTables_ExistAfterMigration()
    {
        // Outbox tabloları migration ile kuruldu (aksi halde ilk randevu oluşturma patlardı); şema burada da doğrulanır.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        Assert.True(await db.Database.CanConnectAsync(Ct));
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('OutboxMessage','OutboxState','InboxState')").ToListAsync(Ct);
        Assert.Equal(3, tables.Count);
    }
}

// RabbitMQ KAPALI (gerçek RabbitMQ taşıması, ulaşılamayan adres): randevu yine alınır, olay OutboxMessage'ta teslimi bekler.
// ÖĞRETİM: senkron çağrıda karşı servis kapalıyken işlem ya başarısız olur ya "hata yutulur, bildirim kaybolur". Outbox'ta ikisi de yok.
public class AppointmentRabbitDownTests : IClassFixture<RabbitDownAppointmentApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly RabbitDownAppointmentApiFactory _factory;

    public AppointmentRabbitDownTests(RabbitDownAppointmentApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_WhenRabbitMqIsDown_StillReturns201_AndEventWaitsInOutbox()
    {
        var patient = $"pat-{Guid.NewGuid():N}";
        var client = _factory.CreateClient().WithBearer(TestJwt.Create(patient, [HastaneRoles.Patient]));

        var response = await client.PostAsJsonAsync("/api/appointments", new CreateAppointmentDto
        {
            DoctorId = $"doc-{Guid.NewGuid():N}",
            BranchId = "branch-1",
            AppointmentDate = DateTime.UtcNow.AddDays(22).Date.AddHours(9),
        }, Json, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResultAppointmentDto>(Json, Ct))!;
        var fetched = await client.GetAsync($"/api/appointments/{created.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        // Olay kaybolmadı: aynı transaction'la outbox tablosuna yazıldı, RabbitMQ yok diye henüz teslim edilmedi.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppointmentDbContext>();
        var pending = await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>()
            .AnyAsync(m => m.Body.Contains(created.Id.ToString()), Ct);
        Assert.True(pending);
    }
}
