using System.Reflection;
using System.Text.Json;
using Hastane.Queue.Api.Hubs;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Hastane.Queue.Tests;

// SignalR hub: anonim bağlanılır, yalnız dinlenir; yayın yalnız sunucudan (yetkili REST uçlarından sonra) gelir.
[Collection(QueueCollection.Name)]
public class QueueHubTests : QueueTestBase
{
    public QueueHubTests(QueueApiFactory factory) : base(factory)
    {
    }

    // TestServer WebSocket istemcisi SignalR'ın ClientWebSocket'ine takılamaz; negotiate + long polling ile bağlanılır
    // (hub ve grup mantığı aktarımdan bağımsızdır). Gateway üzerinden gerçek WebSocket Gateway testinde doğrulanır.
    private async Task<(HubConnection Connection, TaskCompletionSource<JsonElement> First)> ConnectAsync(Func<HubConnection, Task> join)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, "hubs/queue"), options =>
            {
                options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                // Token VERİLMEZ: anonim bağlantı.
            })
            .Build();

        var first = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>(QueueGroups.UpdatedMethod, payload => first.TrySetResult(payload.Clone()));
        await connection.StartAsync(Ct);
        await join(connection);
        return (connection, first);
    }

    private static async Task<JsonElement> WaitAsync(Task<JsonElement> task)
        => await task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

    [Fact]
    public async Task AnonymousClient_JoinDoctor_ReceivesQueueUpdated_AfterDoctorCalls_WithPublicFieldsOnly()
    {
        var doctor = Unique("doc");
        var patient = Unique("pat");
        var appointment = NewAppointment(patient, doctor);
        Assert.Equal(System.Net.HttpStatusCode.Created, (await CheckInAsync(patient, appointment, "Can Yılmaz")).StatusCode);

        var (connection, first) = await ConnectAsync(c => c.InvokeAsync("JoinDoctor", doctor, Ct));
        await using var _ = connection;

        var call = await ClientFor(DoctorToken(doctor)).PostAsync("/api/queue/doctor/call-next", null, Ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, call.StatusCode);

        var payload = await WaitAsync(first.Task);
        Assert.Equal(["calledAt", "displayName", "doctorId", "number", "status"], payload.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(doctor, payload.GetProperty("doctorId").GetString());
        Assert.Equal(1, payload.GetProperty("number").GetInt32());
        Assert.Equal("Can Y.", payload.GetProperty("displayName").GetString());
        Assert.Equal("Called", payload.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, payload.GetProperty("calledAt").ValueKind);

        var raw = payload.GetRawText();
        Assert.DoesNotContain(patient, raw);
        Assert.DoesNotContain(appointment.ToString(), raw);
    }

    [Fact]
    public async Task JoinAll_ReceivesUpdatesOfAnyDoctor_IncludingCheckIn()
    {
        var doctor = Unique("doc");
        var (connection, first) = await ConnectAsync(c => c.InvokeAsync("JoinAll", Ct));
        await using var _ = connection;

        await NewEntryAsync(doctor, "Deniz Aydın");

        var payload = await WaitAsync(first.Task);
        Assert.Equal(doctor, payload.GetProperty("doctorId").GetString());
        Assert.Equal("Waiting", payload.GetProperty("status").GetString());
        Assert.Equal("Deniz A.", payload.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task ClientOfAnotherDoctorGroup_DoesNotReceive()
    {
        var doctor = Unique("doc");
        var (connection, first) = await ConnectAsync(c => c.InvokeAsync("JoinDoctor", Unique("other-doc"), Ct));
        await using var _ = connection;

        await NewEntryAsync(doctor);

        await Assert.ThrowsAsync<TimeoutException>(() => first.Task.WaitAsync(TimeSpan.FromSeconds(1.5), Ct));
    }

    [Fact]
    public async Task Client_CannotBroadcast_ArbitraryHubMethodsDoNotExist()
    {
        var doctor = Unique("doc");
        var (listener, heard) = await ConnectAsync(c => c.InvokeAsync("JoinDoctor", doctor, Ct));
        await using var _l = listener;
        var (attacker, _) = await ConnectAsync(c => c.InvokeAsync("JoinAll", Ct));
        await using var _a = attacker;

        // Sunucuda bu adlarla hub metodu yok: hepsi HubException ile reddedilir.
        foreach (var method in new[] { "QueueUpdated", "Send", "SendAsync", "Broadcast", "Publish", "SendMessage" })
        {
            await Assert.ThrowsAsync<HubException>(() => attacker.InvokeAsync(method, new { doctorId = doctor, number = 99, displayName = "Sahte", status = "Called" }, Ct));
        }

        await Assert.ThrowsAsync<TimeoutException>(() => heard.Task.WaitAsync(TimeSpan.FromSeconds(1.5), Ct));
    }

    [Fact]
    public async Task JoinDoctor_WithInvalidId_IsRejected()
    {
        var (connection, _) = await ConnectAsync(c => Task.CompletedTask);
        await using var _c = connection;

        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinDoctor", "", Ct));
        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinDoctor", new string('x', 65), Ct));
    }

    [Fact]
    public void Hub_ExposesOnlyGroupJoinMethods()
    {
        var methods = typeof(QueueHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Order();

        Assert.Equal(["JoinAll", "JoinDoctor"], methods);
    }
}
