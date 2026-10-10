using System.Net;
using System.Net.Http.Json;
using Hastane.Queue.Api.Dtos;
using Hastane.Queue.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Queue.Tests;

[Collection(QueueCollection.Name)]
public class QueueCheckInTests : QueueTestBase
{
    public QueueCheckInTests(QueueApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CheckIn_ConfirmedToday_Returns201_MaskedName_NumberOne_AndForwardsToken()
    {
        var doctor = Unique("doc");
        var patient = Unique("pat");
        var appointment = NewAppointment(patient, doctor);

        var response = await CheckInAsync(patient, appointment, "Can Yılmaz");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await ReadAsync<MyQueueEntryDto>(response);
        Assert.Equal("Can Y.", entry.DisplayName);
        Assert.Equal(1, entry.Number);
        Assert.Equal(QueueStatus.Waiting, entry.Status);
        Assert.Equal(appointment, entry.AppointmentId);
        Assert.Equal(doctor, entry.DoctorId);
        Assert.Equal(0, entry.AheadCount);
        // Kullanıcının token'ı Appointment servisine aynen iletildi (sahiplik kararı orada).
        Assert.Contains(FakeDownstream.Calls, c => c.Path == $"/api/appointments/{appointment}" && c.Authorization!.StartsWith("Bearer ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CheckIn_FullNameIsNeverStored()
    {
        var patient = Unique("pat");
        var entry = await NewEntryAsync(Unique("doc"), "Zeynep Kaya-Demir", patient);

        var stored = await LoadEntryAsync(entry.Id);

        Assert.Equal("Zeynep K.", stored.DisplayName);
        Assert.DoesNotContain("Demir", stored.DisplayName);
    }

    [Fact]
    public async Task CheckIn_SecondTime_Returns200_WithSameEntry_AndNoDuplicateRow()
    {
        var doctor = Unique("doc");
        var patient = Unique("pat");
        var appointment = NewAppointment(patient, doctor);

        var first = await CheckInAsync(patient, appointment);
        var second = await CheckInAsync(patient, appointment);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var a = await ReadAsync<MyQueueEntryDto>(first);
        var b = await ReadAsync<MyQueueEntryDto>(second);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Number, b.Number);
        Assert.Equal(1, await WithDbAsync(db => db.QueueEntries.CountAsync(e => e.AppointmentId == appointment, Ct)));
    }

    [Fact]
    public async Task CheckIn_SomeoneElsesAppointment_Returns404_AndCreatesNothing()
    {
        var owner = Unique("pat");
        var intruder = Unique("pat");
        var appointment = NewAppointment(owner, Unique("doc"));

        var response = await CheckInAsync(intruder, appointment);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.QueueEntries.CountAsync(e => e.AppointmentId == appointment, Ct)));
    }

    [Fact]
    public async Task CheckIn_AfterOwnerCheckedIn_OtherPatientGets404_NotTheEntry()
    {
        var owner = Unique("pat");
        var appointment = NewAppointment(owner, Unique("doc"));
        Assert.Equal(HttpStatusCode.Created, (await CheckInAsync(owner, appointment)).StatusCode);

        var response = await CheckInAsync(Unique("pat"), appointment);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CheckIn_UnknownAppointment_Returns404()
    {
        var response = await CheckInAsync(Unique("pat"), Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    public async Task CheckIn_NotConfirmed_Returns409(string status)
    {
        var patient = Unique("pat");
        var response = await CheckInAsync(patient, NewAppointment(patient, Unique("doc"), status));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Json, Ct);
        Assert.Contains("onaylanmış", problem!.Detail);
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    public async Task CheckIn_NotToday_Returns409(int dayOffset)
    {
        var patient = Unique("pat");
        var appointment = NewAppointment(patient, Unique("doc"), dateUtc: DateTime.UtcNow.AddDays(dayOffset));

        var response = await CheckInAsync(patient, appointment);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Json, Ct);
        Assert.Contains("bugün", problem!.Detail);
        Assert.Equal(0, await WithDbAsync(db => db.QueueEntries.CountAsync(e => e.AppointmentId == appointment, Ct)));
    }

    [Fact]
    public async Task CheckIn_AppointmentServiceDown_Returns503()
    {
        var patient = Unique("pat");
        var appointment = NewAppointment(patient, Unique("doc"));
        FakeDownstream.Mode = DownstreamMode.Down;

        var response = await CheckInAsync(patient, appointment);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task CheckIn_EmptyGuid_Returns400()
    {
        var response = await ClientFor(PatientToken(Unique("pat")))
            .PostAsJsonAsync("/api/queue/check-in", new { appointmentId = Guid.Empty }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DailyNumber_IsSequentialPerDoctor_AndIndependentBetweenDoctors()
    {
        var doctorA = Unique("doc");
        var doctorB = Unique("doc");

        var a1 = await NewEntryAsync(doctorA);
        var a2 = await NewEntryAsync(doctorA);
        var a3 = await NewEntryAsync(doctorA);
        var b1 = await NewEntryAsync(doctorB);

        Assert.Equal([1, 2, 3], new[] { a1.Number, a2.Number, a3.Number });
        Assert.Equal(1, b1.Number);
        Assert.Equal(2, a3.AheadCount);
    }

    [Fact]
    public async Task ConcurrentCheckIns_EightPatients_GetUniqueNumbersOneToEight()
    {
        var doctor = Unique("doc");
        var patients = Enumerable.Range(0, 8).Select(_ => Unique("pat")).ToArray();
        var appointments = patients.Select(p => NewAppointment(p, doctor)).ToArray();

        var responses = await Task.WhenAll(patients.Select((p, i) => CheckInAsync(p, appointments[i])));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var numbers = new List<int>();
        foreach (var r in responses) numbers.Add((await ReadAsync<MyQueueEntryDto>(r)).Number);
        Assert.Equal(Enumerable.Range(1, 8), numbers.Order());
        // Veritabanındaki gerçek durum da tekil: tekil indeks + yeniden deneme çalıştı.
        var stored = await WithDbAsync(db => db.QueueEntries.Where(e => e.DoctorId == doctor).Select(e => e.Number).ToListAsync(Ct));
        Assert.Equal(Enumerable.Range(1, 8), stored.Order());
    }

    [Fact]
    public async Task ConcurrentCheckIns_SameAppointment_CreateExactlyOneEntry()
    {
        var doctor = Unique("doc");
        var patient = Unique("pat");
        var appointment = NewAppointment(patient, doctor);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CheckInAsync(patient, appointment)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(1, await WithDbAsync(db => db.QueueEntries.CountAsync(e => e.AppointmentId == appointment, Ct)));
    }

    [Fact]
    public async Task MeToday_ReturnsOwnEntriesOnly_WithAheadCount()
    {
        var doctor = Unique("doc");
        await NewEntryAsync(doctor);
        await NewEntryAsync(doctor);
        var mine = Unique("pat");
        var entry = await NewEntryAsync(doctor, patientSub: mine);

        var response = await ClientFor(PatientToken(mine)).GetAsync("/api/queue/me/today", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadAsync<List<MyQueueEntryDto>>(response);
        var only = Assert.Single(list);
        Assert.Equal(entry.Id, only.Id);
        Assert.Equal(3, only.Number);
        Assert.Equal(2, only.AheadCount);
    }
}
