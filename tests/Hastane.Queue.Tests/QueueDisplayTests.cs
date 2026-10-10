using System.Net;
using System.Text.Json;
using Hastane.Queue.Api.Dtos;
using Hastane.Queue.Api.Entities;

namespace Hastane.Queue.Tests;

// Herkese açık ekran ucu: anonim, maskeli, kimliksiz.
[Collection(QueueCollection.Name)]
public class QueueDisplayTests : QueueTestBase
{
    public QueueDisplayTests(QueueApiFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Display_Anonymous_ShowsCurrentAndNextFive_InNumberOrder()
    {
        var doctor = Unique("doc");
        for (var i = 0; i < 8; i++) await NewEntryAsync(doctor, $"Hasta{i} Soyad");
        await ClientFor(DoctorToken(doctor)).PostAsync("/api/queue/doctor/call-next", null, Ct);

        var response = await ClientFor().GetAsync($"/api/queue/display?doctorId={doctor}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var display = await ReadAsync<QueueDisplayDto>(response);
        Assert.Equal(1, display.Current!.Number);
        Assert.Equal(QueueStatus.Called, display.Current.Status);
        Assert.Equal("Hasta0 S.", display.Current.DisplayName);
        Assert.Equal([2, 3, 4, 5, 6], display.Next.Select(n => n.Number));
    }

    [Fact]
    public async Task Display_NoOneCalled_CurrentIsNull()
    {
        var doctor = Unique("doc");
        await NewEntryAsync(doctor);

        var display = await ReadAsync<QueueDisplayDto>(await ClientFor().GetAsync($"/api/queue/display?doctorId={doctor}", Ct));

        Assert.Null(display.Current);
        Assert.Single(display.Next);
    }

    [Fact]
    public async Task Display_ResponseCarriesNoPatientOrAppointmentIdentity_AndNoFullName()
    {
        var doctor = Unique("doc");
        var patient = Unique("pat");
        var appointment = NewAppointment(patient, doctor);
        await CheckInAsync(patient, appointment, "Ayşe Gizlioğlu");
        await NewEntryAsync(doctor, "Mehmet Gizli");
        await ClientFor(DoctorToken(doctor)).PostAsync("/api/queue/doctor/call-next", null, Ct);

        var body = await (await ClientFor().GetAsync($"/api/queue/display?doctorId={doctor}", Ct)).Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain(patient, body);
        Assert.DoesNotContain(appointment.ToString(), body);
        // JSON ASCII dışını \uXXXX kaçırır; soyadın ASCII başı bile görünmemeli.
        Assert.DoesNotContain("Gizlio", body);
        Assert.DoesNotContain("Mehmet Gizli", body);
        Assert.DoesNotContain("patient", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("appointment", body, StringComparison.OrdinalIgnoreCase);

        // Öğe şekli sabit: yalnız bu dört alan; ad maskeli.
        using var doc = JsonDocument.Parse(body);
        var item = doc.RootElement.GetProperty("current");
        Assert.Equal(["calledAt", "displayName", "number", "status"], item.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("Ayşe G.", item.GetProperty("displayName").GetString());
        Assert.Equal("Mehmet G.", doc.RootElement.GetProperty("next")[0].GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Display_OnlyShowsTheRequestedDoctor()
    {
        var doctor = Unique("doc");
        var otherDoctor = Unique("doc");
        await NewEntryAsync(otherDoctor, "Baska Kisi");

        var display = await ReadAsync<QueueDisplayDto>(await ClientFor().GetAsync($"/api/queue/display?doctorId={doctor}", Ct));

        Assert.Null(display.Current);
        Assert.Empty(display.Next);
    }

    [Fact]
    public async Task Display_CompletedAndNoShow_AreNotListed()
    {
        var doctor = Unique("doc");
        var token = DoctorToken(doctor);
        var first = await NewEntryAsync(doctor);
        var second = await NewEntryAsync(doctor);
        await ClientFor(token).PostAsync($"/api/queue/{first.Id}/no-show", null, Ct);
        await ClientFor(token).PostAsync($"/api/queue/{second.Id}/call", null, Ct);
        await ClientFor(token).PostAsync($"/api/queue/{second.Id}/complete", null, Ct);

        var display = await ReadAsync<QueueDisplayDto>(await ClientFor().GetAsync($"/api/queue/display?doctorId={doctor}", Ct));

        Assert.Null(display.Current);
        Assert.Empty(display.Next);
    }

    [Theory]
    [InlineData("/api/queue/display")]
    [InlineData("/api/queue/display?doctorId=")]
    public async Task Display_WithoutDoctorId_Returns400(string url)
        => Assert.Equal(HttpStatusCode.BadRequest, (await ClientFor().GetAsync(url, Ct)).StatusCode);
}
