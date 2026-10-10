using Hastane.Chat.Api.Integration;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Hastane.Chat.Tests;

// Test sınıfı başına gerçek bir MongoDB konteyneri; Appointment/Doctor/Identity çağrıları sahte (FakeDownstream).
public class ChatApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7").Build();

    public FakeDownstream Downstream { get; } = new();

    public string MongoConnectionString => _mongo.GetConnectionString();

    public async ValueTask InitializeAsync() => await _mongo.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestJwt();
        builder.UseSetting("DatabaseSettings:DatabaseName", "hastane_chat_test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMongoClient>();
            services.AddSingleton<IMongoClient>(_ => new MongoClient(_mongo.GetConnectionString()));
            services.RemoveAll<IAppointmentLookup>();
            services.RemoveAll<IDoctorLookup>();
            services.RemoveAll<IUserLookup>();
            services.AddSingleton<IAppointmentLookup>(Downstream);
            services.AddSingleton<IDoctorLookup>(Downstream);
            services.AddSingleton<IUserLookup>(Downstream);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _mongo.DisposeAsync();
    }
}

// Üç karşı servisin tek sahte sınıfı. Randevu = (hasta kimliği, doktor kimliği) çiftleri; Down bayrakları "kapalı" yapar.
public class FakeDownstream : IAppointmentLookup, IDoctorLookup, IUserLookup
{
    private readonly HashSet<(string Patient, string Doctor)> _appointments = [];
    private readonly Dictionary<string, PublicDoctorInfo> _doctors = [];
    private readonly Dictionary<string, UserInfo> _users = [];

    public bool AppointmentDown { get; set; }
    public bool IdentityDown { get; set; }

    public void AddAppointment(string patientUserId, string doctorId) => _appointments.Add((patientUserId, doctorId));
    public void AddDoctor(string doctorId, string first, string last, string? title = null) => _doctors[doctorId] = new(doctorId, first, last, title);
    public void AddUser(string userId, string first, string last) => _users[userId] = new(userId, first, last);

    public Task<LookupResult<List<AppointmentRef>>> ListAsync(string? doctorId, string? patientId, CancellationToken ct)
    {
        if (AppointmentDown) return Task.FromResult(LookupResult<List<AppointmentRef>>.Unavailable());
        var hits = _appointments.Where(a => a.Doctor == doctorId && a.Patient == patientId).Select(_ => new AppointmentRef(Guid.NewGuid())).ToList();
        return Task.FromResult(LookupResult<List<AppointmentRef>>.Found(hits));
    }

    public Task<LookupResult<PublicDoctorInfo>> GetPublicAsync(string doctorId, CancellationToken ct)
        => Task.FromResult(_doctors.TryGetValue(doctorId, out var d) ? LookupResult<PublicDoctorInfo>.Found(d) : LookupResult<PublicDoctorInfo>.NotFound());

    public Task<LookupResult<List<UserInfo>>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken ct)
        => Task.FromResult(IdentityDown
            ? LookupResult<List<UserInfo>>.Unavailable()
            : LookupResult<List<UserInfo>>.Found(ids.Where(_users.ContainsKey).Select(i => _users[i]).ToList()));
}
