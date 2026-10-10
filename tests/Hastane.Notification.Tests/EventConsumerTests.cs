using System.Net.Http.Json;
using Hastane.Contracts;
using Hastane.Notification.Api.Dtos;
using Hastane.Notification.Api.Messaging;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Hastane.Notification.Tests.NotificationTestHelpers;

namespace Hastane.Notification.Tests;

// Olay testleri için ayrı fabrika (ayrı Mongo konteyneri). Test harness kaydı temel sınıfta (NotificationApiFactory):
// üretimdeki tüketici yapılandırması + MassTransit test harness (bellek içi taşıma; RabbitMQ yok). Mongo gerçek (konteyner).
public class NotificationEventApiFactory : NotificationApiFactory;

// Olay -> uygulama içi bildirim: metinler/alıcılar eski senkron yolla aynı, aynı olay iki kez gelirse tek bildirim.
public class EventConsumerTests : IClassFixture<NotificationEventApiFactory>
{
    private readonly NotificationEventApiFactory _factory;

    public EventConsumerTests(NotificationEventApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private ITestHarness Harness => _factory.Services.GetTestHarness();

    private HttpClient PatientClient(string sub)
        => _factory.CreateClient().WithBearer(TestJwt.Create(sub, [HastaneRoles.Patient]));

    private HttpClient DoctorClient(string doctorId)
        => _factory.CreateClient().WithBearer(TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], doctorId));

    private static async Task<List<ResultNotificationDto>> MineAsync(HttpClient client)
        => (await client.GetFromJsonAsync<NotificationPageDto>("/api/notifications/me", Json, Ct))!.Items;

    // Tüketicinin bu mesajı n kez İŞLEMİŞ olmasını bekler (Consumed, tüketici bitince dolar).
    private async Task WaitConsumedAsync<T>(Guid eventId, Func<T, Guid> idOf, int times = 1) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Harness.Consumed.Select<T>(m => idOf(m.Context.Message) == eventId, Ct).Count() < times)
        {
            Assert.True(DateTime.UtcNow < deadline, "Tüketici zamanında işlemedi.");
            await Task.Delay(50, Ct);
        }
    }

    private static AppointmentCreated NewCreated(string patient, string doctor, DateTime? date = null) => new()
    {
        EventId = Guid.NewGuid(),
        AppointmentId = Guid.NewGuid(),
        PatientUserId = patient,
        DoctorId = doctor,
        AppointmentDate = date ?? new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task AppointmentCreated_NotifiesPatientAndDoctor_WithClinicLocalTime()
    {
        var patient = Unique("pat");
        var doctor = Unique("doc");
        var e = NewCreated(patient, doctor); // 09:00 UTC = 12:00 İstanbul

        await Harness.Bus.Publish(e, Ct);
        await WaitConsumedAsync<AppointmentCreated>(e.EventId, m => m.EventId);

        var toPatient = Assert.Single(await MineAsync(PatientClient(patient)));
        Assert.Equal("Randevunuz alındı", toPatient.Title);
        Assert.Equal("Randevunuz alındı: 01.12.2026 12:00", toPatient.Message);
        Assert.Equal($"/Patient/Appointments/Details/{e.AppointmentId}", toPatient.Link);
        Assert.Equal("appointment-created", toPatient.Kind);

        var toDoctor = Assert.Single(await MineAsync(DoctorClient(doctor)));
        Assert.Equal("Yeni randevu talebi", toDoctor.Title);
        Assert.Equal("Yeni randevu talebi: 01.12.2026 12:00", toDoctor.Message);
        Assert.Equal($"/Doctor/Appointments/Details/{e.AppointmentId}", toDoctor.Link);
    }

    [Fact]
    public async Task SameEventDeliveredTwice_ProducesSingleNotificationPerRecipient()
    {
        var patient = Unique("pat");
        var doctor = Unique("doc");
        var e = NewCreated(patient, doctor);

        // RabbitMQ "en az bir kez" teslim eder: aynı EventId ikinci kez gelebilir.
        await Harness.Bus.Publish(e, Ct);
        await Harness.Bus.Publish(e, Ct);
        await WaitConsumedAsync<AppointmentCreated>(e.EventId, m => m.EventId, times: 2);

        Assert.Single(await MineAsync(PatientClient(patient)));
        Assert.Single(await MineAsync(DoctorClient(doctor)));
    }

    [Fact]
    public async Task DifferentEvents_ForSameAppointment_AreNotDeduplicated()
    {
        var patient = Unique("pat");
        var doctor = Unique("doc");
        var first = NewCreated(patient, doctor);
        var second = first with { EventId = Guid.NewGuid() }; // aynı randevu, FARKLI olay

        await Harness.Bus.Publish(first, Ct);
        await Harness.Bus.Publish(second, Ct);
        await WaitConsumedAsync<AppointmentCreated>(first.EventId, m => m.EventId);
        await WaitConsumedAsync<AppointmentCreated>(second.EventId, m => m.EventId);

        Assert.Equal(2, (await MineAsync(PatientClient(patient))).Count);
    }

    [Fact]
    public async Task StatusChangedByDoctor_NotifiesPatientOnly()
    {
        var patient = Unique("pat");
        var doctor = Unique("doc");
        var e = new AppointmentStatusChanged
        {
            EventId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            PatientUserId = patient,
            DoctorId = doctor,
            AppointmentDate = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc),
            OldStatus = "Pending",
            NewStatus = "Confirmed",
            ChangedByRole = "Doctor",
        };

        await Harness.Bus.Publish(e, Ct);
        await WaitConsumedAsync<AppointmentStatusChanged>(e.EventId, m => m.EventId);

        var n = Assert.Single(await MineAsync(PatientClient(patient)));
        Assert.Equal("Randevu durumu değişti", n.Title);
        Assert.Equal("Randevunuz onaylandı: 01.12.2026 12:00", n.Message);
        Assert.Equal($"/Patient/Appointments/Details/{e.AppointmentId}", n.Link);
        Assert.Empty(await MineAsync(DoctorClient(doctor)));
    }

    [Fact]
    public async Task StatusChangedByPatient_NotifiesDoctorOnly()
    {
        var patient = Unique("pat");
        var doctor = Unique("doc");
        var e = new AppointmentStatusChanged
        {
            EventId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            PatientUserId = patient,
            DoctorId = doctor,
            AppointmentDate = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc),
            OldStatus = "Pending",
            NewStatus = "Cancelled",
            ChangedByRole = "Patient",
        };

        await Harness.Bus.Publish(e, Ct);
        await WaitConsumedAsync<AppointmentStatusChanged>(e.EventId, m => m.EventId);

        var n = Assert.Single(await MineAsync(DoctorClient(doctor)));
        Assert.Equal("Hasta randevuyu iptal etti: 01.12.2026 12:00", n.Message);
        Assert.Equal($"/Doctor/Appointments/Details/{e.AppointmentId}", n.Link);
        Assert.Empty(await MineAsync(PatientClient(patient)));
    }

    [Fact]
    public async Task StatusChangedByAdmin_NotifiesPatient_WithCancelledText()
    {
        var patient = Unique("pat");
        var e = new AppointmentStatusChanged
        {
            EventId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            PatientUserId = patient,
            DoctorId = Unique("doc"),
            AppointmentDate = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc),
            OldStatus = "Confirmed",
            NewStatus = "Cancelled",
            ChangedByRole = "Admin",
        };

        await Harness.Bus.Publish(e, Ct);
        await WaitConsumedAsync<AppointmentStatusChanged>(e.EventId, m => m.EventId);

        var n = Assert.Single(await MineAsync(PatientClient(patient)));
        Assert.Equal("Randevunuz iptal edildi: 01.12.2026 12:00", n.Message);
    }

    [Fact]
    public async Task Rescheduled_NotifiesBoth_WithNewDate()
    {
        var patient = Unique("pat");
        var doctor = Unique("doc");
        var e = new AppointmentRescheduled
        {
            EventId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            PatientUserId = patient,
            DoctorId = doctor,
            OldDate = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc),
            NewDate = new DateTime(2026, 12, 2, 10, 30, 0, DateTimeKind.Utc),
            AppointmentDate = new DateTime(2026, 12, 2, 10, 30, 0, DateTimeKind.Utc),
        };

        await Harness.Bus.Publish(e, Ct);
        await WaitConsumedAsync<AppointmentRescheduled>(e.EventId, m => m.EventId);

        Assert.Equal("Randevunuz yeniden planlandı: 02.12.2026 13:30", Assert.Single(await MineAsync(PatientClient(patient))).Message);
        Assert.Equal("Randevu yeniden planlandı: 02.12.2026 13:30", Assert.Single(await MineAsync(DoctorClient(doctor))).Message);
    }

    [Fact]
    public async Task OrderPlaced_And_OrderCancelled_NotifyPatient()
    {
        var patient = Unique("pat");
        var orderId = Guid.NewGuid();
        var placed = new OrderPlaced { EventId = Guid.NewGuid(), OrderId = orderId, OrderNumber = "SIP-2026-000042", PatientUserId = patient, TotalAmount = 250m, Currency = "TRY" };
        var cancelled = new OrderCancelled { EventId = Guid.NewGuid(), OrderId = orderId, OrderNumber = "SIP-2026-000042", PatientUserId = patient, Reason = "Hasta tarafından iptal edildi" };

        await Harness.Bus.Publish(placed, Ct);
        await Harness.Bus.Publish(cancelled, Ct);
        await WaitConsumedAsync<OrderPlaced>(placed.EventId, m => m.EventId);
        await WaitConsumedAsync<OrderCancelled>(cancelled.EventId, m => m.EventId);

        var mine = await MineAsync(PatientClient(patient));
        Assert.Equal(2, mine.Count);
        var p = Assert.Single(mine, n => n.Kind == "order-placed");
        Assert.Equal("Siparişiniz alındı: SIP-2026-000042", p.Message);
        Assert.Equal($"/Patient/Orders/Details/{orderId}", p.Link);
        var c = Assert.Single(mine, n => n.Kind == "order-cancelled");
        Assert.Equal("Siparişiniz iptal edildi: SIP-2026-000042", c.Message);
    }

    [Fact]
    public async Task OrderPlaced_DeliveredTwice_ProducesSingleNotification()
    {
        var patient = Unique("pat");
        var placed = new OrderPlaced { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), OrderNumber = "SIP-2026-000043", PatientUserId = patient, TotalAmount = 10m, Currency = "TRY" };

        await Harness.Bus.Publish(placed, Ct);
        await Harness.Bus.Publish(placed, Ct);
        await WaitConsumedAsync<OrderPlaced>(placed.EventId, m => m.EventId, times: 2);

        Assert.Single(await MineAsync(PatientClient(patient)));
    }
}

// Saf fonksiyon testleri (Docker gerekmez): olay -> bildirim tarifi.
public class EventNotificationFactoryTests
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    [Fact]
    public void LocalDate_ConvertsUtcToClinicTime_AndAcceptsUnspecifiedKind()
    {
        Assert.Equal("01.12.2026 12:00", EventNotificationFactory.LocalDate(new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc), Istanbul));
        Assert.Equal("01.12.2026 12:00", EventNotificationFactory.LocalDate(new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Unspecified), Istanbul));
    }

    [Fact]
    public void SourceKeys_AreStablePerEventAndRecipient_AndDifferBetweenRecipients()
    {
        var e = new AppointmentCreated { EventId = Guid.NewGuid(), AppointmentId = Guid.NewGuid(), PatientUserId = "p", DoctorId = "d", AppointmentDate = DateTime.UtcNow };

        var first = EventNotificationFactory.For(e, Istanbul);
        var again = EventNotificationFactory.For(e, Istanbul);

        Assert.Equal(first.Select(n => n.SourceKey), again.Select(n => n.SourceKey));
        Assert.Equal(2, first.Select(n => n.SourceKey).Distinct().Count());
    }

    [Fact]
    public void StatusChanged_UnknownStatus_UsesGenericText()
    {
        var e = new AppointmentStatusChanged
        {
            EventId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            PatientUserId = "p",
            DoctorId = "d",
            AppointmentDate = new DateTime(2026, 12, 1, 9, 0, 0, DateTimeKind.Utc),
            NewStatus = "Whatever",
            ChangedByRole = "Doctor",
        };

        var n = Assert.Single(EventNotificationFactory.For(e, Istanbul));
        Assert.StartsWith("Randevunuz güncellendi:", n.Dto.Message);
    }
}
