using System.Net;
using Hastane.Queue.Api.Dtos;
using Hastane.Queue.Api.Entities;

namespace Hastane.Queue.Tests;

// Çağır / tamamla / gelmedi kuralları ve yetkisi.
[Collection(QueueCollection.Name)]
public class QueueFlowTests : QueueTestBase
{
    public QueueFlowTests(QueueApiFactory factory) : base(factory)
    {
    }

    private Task<HttpResponseMessage> Post(string token, string url) => ClientFor(token).PostAsync(url, null, Ct);

    [Fact]
    public async Task CallNext_CallsLowestWaitingNumber_AndSetsCalledAt()
    {
        var doctor = Unique("doc");
        var first = await NewEntryAsync(doctor);
        await NewEntryAsync(doctor);

        var response = await Post(DoctorToken(doctor), "/api/queue/doctor/call-next");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var called = await ReadAsync<QueueEntryDto>(response);
        Assert.Equal(first.Id, called.Id);
        Assert.Equal(QueueStatus.Called, called.Status);
        Assert.NotNull(called.CalledAt);
    }

    [Fact]
    public async Task CallNext_WhileSomeoneIsCalled_Returns409_AndDoesNotAutoComplete()
    {
        var doctor = Unique("doc");
        var first = await NewEntryAsync(doctor);
        var second = await NewEntryAsync(doctor);
        var token = DoctorToken(doctor);
        Assert.Equal(HttpStatusCode.OK, (await Post(token, "/api/queue/doctor/call-next")).StatusCode);

        var again = await Post(token, "/api/queue/doctor/call-next");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(QueueStatus.Called, (await LoadEntryAsync(first.Id)).Status);   // öncekini sessizce tamamlamadı
        Assert.Equal(QueueStatus.Waiting, (await LoadEntryAsync(second.Id)).Status);
    }

    [Fact]
    public async Task Complete_ThenCallNext_MovesToNextPatient()
    {
        var doctor = Unique("doc");
        var first = await NewEntryAsync(doctor);
        var second = await NewEntryAsync(doctor);
        var token = DoctorToken(doctor);
        await Post(token, "/api/queue/doctor/call-next");

        var complete = await Post(token, $"/api/queue/{first.Id}/complete");
        var next = await Post(token, "/api/queue/doctor/call-next");

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var done = await LoadEntryAsync(first.Id);
        Assert.Equal(QueueStatus.Completed, done.Status);
        Assert.NotNull(done.CompletedAt);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(second.Id, (await ReadAsync<QueueEntryDto>(next)).Id);
    }

    [Fact]
    public async Task NoShow_OnCalled_FreesTheSlotForNextCall()
    {
        var doctor = Unique("doc");
        var first = await NewEntryAsync(doctor);
        await NewEntryAsync(doctor);
        var token = DoctorToken(doctor);
        await Post(token, "/api/queue/doctor/call-next");

        var noShow = await Post(token, $"/api/queue/{first.Id}/no-show");

        Assert.Equal(HttpStatusCode.OK, noShow.StatusCode);
        Assert.Equal(QueueStatus.NoShow, (await LoadEntryAsync(first.Id)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Post(token, "/api/queue/doctor/call-next")).StatusCode);
    }

    [Fact]
    public async Task NoShow_OnWaiting_Works()
    {
        var doctor = Unique("doc");
        var entry = await NewEntryAsync(doctor);

        var response = await Post(DoctorToken(doctor), $"/api/queue/{entry.Id}/no-show");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Complete_OnWaiting_Returns409()
    {
        var doctor = Unique("doc");
        var entry = await NewEntryAsync(doctor);

        var response = await Post(DoctorToken(doctor), $"/api/queue/{entry.Id}/complete");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Call_OnAlreadyFinishedEntry_Returns409()
    {
        var doctor = Unique("doc");
        var entry = await NewEntryAsync(doctor);
        var token = DoctorToken(doctor);
        await Post(token, $"/api/queue/{entry.Id}/no-show");

        Assert.Equal(HttpStatusCode.Conflict, (await Post(token, $"/api/queue/{entry.Id}/call")).StatusCode);
    }

    [Fact]
    public async Task Call_SpecificEntry_OutOfOrder_IsAllowed()
    {
        var doctor = Unique("doc");
        await NewEntryAsync(doctor);
        var second = await NewEntryAsync(doctor);

        var response = await Post(DoctorToken(doctor), $"/api/queue/{second.Id}/call");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CallNext_WithEmptyQueue_Returns409()
    {
        var response = await Post(DoctorToken(Unique("doc")), "/api/queue/doctor/call-next");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentCallNext_ExactlyOneWins()
    {
        var doctor = Unique("doc");
        for (var i = 0; i < 4; i++) await NewEntryAsync(doctor);
        var token = DoctorToken(doctor);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Post(token, "/api/queue/doctor/call-next")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    // --- Yetki: başka doktor 404 (varlığı sızmaz), hasta 403, anonim 401, Admin her kayda ---

    [Theory]
    [InlineData("call")]
    [InlineData("complete")]
    [InlineData("no-show")]
    public async Task OtherDoctor_CannotTouchEntry_Returns404_AndEntryUnchanged(string action)
    {
        var owner = Unique("doc");
        var entry = await NewEntryAsync(owner);
        if (action == "complete") await Post(DoctorToken(owner), $"/api/queue/{entry.Id}/call");
        var before = (await LoadEntryAsync(entry.Id)).Status;

        var response = await Post(DoctorToken(Unique("doc")), $"/api/queue/{entry.Id}/{action}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, (await LoadEntryAsync(entry.Id)).Status);
    }

    [Fact]
    public async Task DoctorWithoutDoctorId_CallNext_Returns403()
    {
        var token = Hastane.TestSupport.TestJwt.Create(Unique("usr"), [Hastane.Shared.Security.HastaneRoles.Doctor]);

        Assert.Equal(HttpStatusCode.Forbidden, (await Post(token, "/api/queue/doctor/call-next")).StatusCode);
    }

    [Fact]
    public async Task Admin_CanCallAnyDoctorsEntry()
    {
        var doctor = Unique("doc");
        var entry = await NewEntryAsync(doctor);

        var response = await Post(AdminToken(), $"/api/queue/{entry.Id}/call");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("call")]
    [InlineData("complete")]
    [InlineData("no-show")]
    public async Task Patient_CannotCallCompleteOrNoShow_Returns403(string action)
    {
        var patient = Unique("pat");
        var entry = await NewEntryAsync(Unique("doc"), patientSub: patient);

        var response = await Post(PatientToken(patient), $"/api/queue/{entry.Id}/{action}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(QueueStatus.Waiting, (await LoadEntryAsync(entry.Id)).Status);
    }

    [Fact]
    public async Task Patient_CannotCallNext_Returns403()
        => Assert.Equal(HttpStatusCode.Forbidden, (await Post(PatientToken(Unique("pat")), "/api/queue/doctor/call-next")).StatusCode);

    [Fact]
    public async Task Admin_CannotUseCallNext_Returns403()
        => Assert.Equal(HttpStatusCode.Forbidden, (await Post(AdminToken(), "/api/queue/doctor/call-next")).StatusCode);

    [Fact]
    public async Task DoctorToday_DoctorSeesOwnQueueOnly_QueryDoctorIdIsIgnored()
    {
        var mine = Unique("doc");
        var other = Unique("doc");
        var myEntry = await NewEntryAsync(mine);
        await NewEntryAsync(other);

        var response = await ClientFor(DoctorToken(mine)).GetAsync($"/api/queue/doctor/today?doctorId={other}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await ReadAsync<List<QueueEntryDto>>(response);
        Assert.Equal(myEntry.Id, Assert.Single(list).Id);
    }

    [Fact]
    public async Task DoctorToday_Admin_NeedsDoctorId()
    {
        var doctor = Unique("doc");
        var entry = await NewEntryAsync(doctor);
        var admin = ClientFor(AdminToken());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/queue/doctor/today", Ct)).StatusCode);
        var ok = await admin.GetAsync($"/api/queue/doctor/today?doctorId={doctor}", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(entry.Id, Assert.Single(await ReadAsync<List<QueueEntryDto>>(ok)).Id);
    }

    [Fact]
    public async Task DoctorToday_Patient_Returns403()
        => Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(PatientToken(Unique("pat"))).GetAsync("/api/queue/doctor/today", Ct)).StatusCode);

    [Fact]
    public async Task DoctorTodayResponse_DoesNotContainPatientIdentity()
    {
        var doctor = Unique("doc");
        var patient = Unique("pat");
        var entry = await NewEntryAsync(doctor, patientSub: patient);

        var body = await (await ClientFor(DoctorToken(doctor)).GetAsync("/api/queue/doctor/today", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain(patient, body);
        Assert.DoesNotContain("patientUserId", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(entry.Id.ToString(), body);
    }
}
