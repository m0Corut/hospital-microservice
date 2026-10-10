using System.Net;
using System.Text.Json;
using Hastane.Contracts;
using Hastane.Queue.Api.Dtos;
using Hastane.Queue.Api.Entities;
using Hastane.Queue.Api.Hubs;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Queue.Tests;

// AppointmentStatusChanged(Cancelled) olayı -> bekleyen sıra kaydı Cancelled + SignalR yayını. Taşıma: MassTransit test harness.
[Collection(QueueCollection.Name)]
public class QueueEventTests : QueueTestBase
{
    public QueueEventTests(QueueApiFactory factory) : base(factory)
    {
    }

    private ITestHarness Harness => Factory.Services.GetTestHarness();

    private static AppointmentStatusChanged Event(Guid appointmentId, string patient, string doctor, string newStatus) => new()
    {
        EventId = Guid.NewGuid(),
        AppointmentId = appointmentId,
        PatientUserId = patient,
        DoctorId = doctor,
        AppointmentDate = DateTime.UtcNow,
        OldStatus = "Confirmed",
        NewStatus = newStatus,
        ChangedByRole = "Patient",
    };

    // Tüketici olayı n kez İŞLEMİŞ olana dek bekler (Consumed, tüketici bitince dolar).
    private async Task PublishAndWaitAsync(AppointmentStatusChanged e, int times = 1)
    {
        for (var i = 0; i < times; i++) await Harness.Bus.Publish(e, Ct);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Harness.Consumed.Select<AppointmentStatusChanged>(m => m.Context.Message.EventId == e.EventId, Ct).Count() < times)
        {
            Assert.True(DateTime.UtcNow < deadline, "Tüketici zamanında işlemedi.");
            await Task.Delay(50, Ct);
        }
    }

    private async Task<(Guid AppointmentId, string Patient, MyQueueEntryDto Entry)> CheckedInAsync(string doctor)
    {
        var patient = Unique("pat");
        var appointment = NewAppointment(patient, doctor);
        var response = await CheckInAsync(patient, appointment);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (appointment, patient, await ReadAsync<MyQueueEntryDto>(response));
    }

    [Fact]
    public async Task Cancelled_MarksWaitingEntryCancelled_AndAheadCountsOfOthersDrop()
    {
        var doctor = Unique("doc");
        var first = await CheckedInAsync(doctor);
        var second = await CheckedInAsync(doctor);
        Assert.Equal(1, second.Entry.AheadCount);

        await PublishAndWaitAsync(Event(first.AppointmentId, first.Patient, doctor, "Cancelled"));

        Assert.Equal(QueueStatus.Cancelled, (await LoadEntryAsync(first.Entry.Id)).Status);
        Assert.Equal(QueueStatus.Waiting, (await LoadEntryAsync(second.Entry.Id)).Status);
        // İkinci hastanın önündeki bekleyen sayısı düştü: iptal edilen artık "Waiting" değil.
        var mine = await ReadAsync<List<MyQueueEntryDto>>(await ClientFor(PatientToken(second.Patient)).GetAsync("/api/queue/me/today", Ct));
        Assert.Equal(0, Assert.Single(mine).AheadCount);
    }

    [Fact]
    public async Task Cancelled_BroadcastsQueueUpdated_WithCancelledStatus()
    {
        var doctor = Unique("doc");
        var item = await CheckedInAsync(doctor);

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, "hubs/queue"), options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>(QueueGroups.UpdatedMethod, payload => received.TrySetResult(payload.Clone()));
        await connection.StartAsync(Ct);
        await using var _ = connection;
        await connection.InvokeAsync("JoinDoctor", doctor, Ct);

        await PublishAndWaitAsync(Event(item.AppointmentId, item.Patient, doctor, "Cancelled"));

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("Cancelled", payload.GetProperty("status").GetString());
        Assert.Equal(doctor, payload.GetProperty("doctorId").GetString());
        Assert.DoesNotContain(item.Patient, payload.GetRawText()); // kimlik sızmaz
    }

    [Fact]
    public async Task Cancelled_DeliveredTwice_IsIdempotent()
    {
        var doctor = Unique("doc");
        var item = await CheckedInAsync(doctor);

        await PublishAndWaitAsync(Event(item.AppointmentId, item.Patient, doctor, "Cancelled"), times: 2);

        Assert.Equal(QueueStatus.Cancelled, (await LoadEntryAsync(item.Entry.Id)).Status);
        Assert.False(await Harness.Published.Any<Fault<AppointmentStatusChanged>>(m => m.Context.Message.Message.AppointmentId == item.AppointmentId, Ct));
    }

    [Theory]
    [InlineData("Confirmed")]
    [InlineData("Completed")]
    [InlineData("Pending")]
    public async Task OtherStatuses_DoNotTouchTheQueueEntry(string newStatus)
    {
        var doctor = Unique("doc");
        var item = await CheckedInAsync(doctor);

        await PublishAndWaitAsync(Event(item.AppointmentId, item.Patient, doctor, newStatus));

        Assert.Equal(QueueStatus.Waiting, (await LoadEntryAsync(item.Entry.Id)).Status);
    }

    [Fact]
    public async Task Cancelled_DoesNotCancelAnAlreadyCalledEntry()
    {
        var doctor = Unique("doc");
        var item = await CheckedInAsync(doctor);
        var call = await ClientFor(DoctorToken(doctor)).PostAsync("/api/queue/doctor/call-next", null, Ct);
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);

        await PublishAndWaitAsync(Event(item.AppointmentId, item.Patient, doctor, "Cancelled"));

        Assert.Equal(QueueStatus.Called, (await LoadEntryAsync(item.Entry.Id)).Status);
    }

    [Fact]
    public async Task Cancelled_ForAppointmentWithoutQueueEntry_IsHarmless()
    {
        var e = Event(Guid.CreateVersion7(), Unique("pat"), Unique("doc"), "Cancelled");

        await PublishAndWaitAsync(e);

        Assert.False(await Harness.Published.Any<Fault<AppointmentStatusChanged>>(m => m.Context.Message.Message.EventId == e.EventId, Ct));
    }
}
