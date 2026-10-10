using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Queue.Api.Data;
using Hastane.Queue.Api.Dtos;
using Hastane.Queue.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Queue.Tests;

public abstract class QueueTestBase
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected readonly QueueApiFactory Factory;

    protected QueueTestBase(QueueApiFactory factory)
    {
        Factory = factory;
        FakeDownstream.Reset();
    }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    protected HttpClient ClientFor(string? token = null)
    {
        var client = Factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    protected static string PatientToken(string sub, string name = "Can Yılmaz")
        => TestJwt.Create(sub, [HastaneRoles.Patient], name: name);

    protected static string DoctorToken(string doctorId) => TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], doctorId);

    protected static string AdminToken() => TestJwt.Create(Unique("adm"), [HastaneRoles.Admin]);

    // Sahte Appointment servisinde bir randevu kurar (varsayılan: onaylı ve bugün).
    protected static Guid NewAppointment(string patientId, string doctorId, string status = "Confirmed", DateTime? dateUtc = null)
    {
        var id = Guid.CreateVersion7();
        FakeDownstream.RegisterAppointment(id, patientId, doctorId, status, dateUtc ?? DateTime.UtcNow);
        return id;
    }

    protected Task<HttpResponseMessage> CheckInAsync(string patientSub, Guid appointmentId, string name = "Can Yılmaz")
        => ClientFor(PatientToken(patientSub, name)).PostAsJsonAsync("/api/queue/check-in", new { appointmentId }, Ct);

    // Yeni hasta + onaylı randevu + check-in; oluşan kaydı döndürür.
    protected async Task<MyQueueEntryDto> NewEntryAsync(string doctorId, string name = "Can Yılmaz", string? patientSub = null)
    {
        var patient = patientSub ?? Unique("pat");
        var response = await CheckInAsync(patient, NewAppointment(patient, doctorId), name);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync<MyQueueEntryDto>(response);
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;

    protected async Task<T> WithDbAsync<T>(Func<QueueDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<QueueDbContext>());
    }

    protected Task<QueueEntry> LoadEntryAsync(Guid id)
        => WithDbAsync(db => db.QueueEntries.AsNoTracking().SingleAsync(e => e.Id == id, Ct));
}
