using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hastane.Patient.Api.Data;
using Hastane.Patient.Api.Dtos;
using Hastane.Patient.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Patient.Tests;

public abstract class PatientTestBase : IDisposable
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected readonly PatientApiFactory Factory;

    protected PatientTestBase(PatientApiFactory factory)
    {
        Factory = factory;
        FakeDownstream.Reset();
    }

    public void Dispose() => FakeDownstream.Reset();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    protected HttpClient ClientFor(string? token = null)
    {
        var client = Factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    protected static string PatientToken(string sub) => TestJwt.Create(sub, [HastaneRoles.Patient]);
    protected static string DoctorToken(string doctorId) => TestJwt.Create(Unique("usr"), [HastaneRoles.Doctor], doctorId);
    protected static string AdminToken() => TestJwt.Create(Unique("adm"), [HastaneRoles.Admin]);

    protected static StringContent RawJson(string json) => new(json, Encoding.UTF8, "application/json");

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;

    // Geçerli, eksiksiz bir profil gövdesi.
    protected static UpdatePatientProfileDto FullProfile() => new()
    {
        DateOfBirth = new DateOnly(1990, 5, 17),
        Gender = Gender.Female,
        BloodType = BloodType.APos,
        Phone = "+90 532 111 22 33",
        Address = "Atatürk Cad. No:1 D:2",
        City = "Ankara",
        EmergencyContactName = "Ayşe Yılmaz",
        EmergencyContactPhone = "0532 444 55 66",
    };

    protected async Task<T> WithDbAsync<T>(Func<PatientDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<PatientDbContext>());
    }
}
