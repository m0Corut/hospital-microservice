using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Hastane.Doctor.Api.Activity;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Hastane.Doctor.Tests.TestSupport;

namespace Hastane.Doctor.Tests;

// Olay akışı (Kafka) üretici tarafı. Bu testler Kafka'ya DOKUNMAZ: sözleşme, kanal davranışı ve "Kafka kapalıyken istek bozulmaz"
// burada doğrulanır. Uçtan uca (gerçek Kafka) test Analytics test projesindedir.
public class DoctorActivityTests : IClassFixture<DoctorApiFactory>
{
    private readonly DoctorApiFactory _factory;

    public DoctorActivityTests(DoctorApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingPublisher : IDoctorActivityPublisher
    {
        public List<DoctorActivityEvent> Events { get; } = [];

        public bool TryPublish(DoctorActivityEvent activity)
        {
            lock (Events) Events.Add(activity);
            return true;
        }
    }

    private HttpClient AdminClient(HttpClient c) => c.WithBearer(TestJwt.Create("admin-1", [HastaneRoles.Admin]));

    // Sahte yayıncılı istemci: kanala yazılan olayları kaydeder.
    private (HttpClient Client, RecordingPublisher Recorder) WithRecorder(string? role = null)
    {
        var recorder = new RecordingPublisher();
        var client = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IDoctorActivityPublisher>();
            s.AddSingleton<IDoctorActivityPublisher>(recorder);
        })).CreateClient();
        if (role is not null) client.WithBearer(TestJwt.Create("user-1", [role]));
        return (client, recorder);
    }

    private async Task<ResultDoctorDto> CreateDoctorAsync()
        => await AdminClient(_factory.CreateClient()).CreateOk(NewDoc("Olay", NewTag(), "b", DoctorGender.Female, Loc("Ankara", 300m)), Ct);

    // ---------- olay kanala yazılıyor ----------

    [Fact]
    public async Task Search_writes_one_anonymous_search_event_with_text_filters_count_and_engine()
    {
        var doctor = await CreateDoctorAsync();
        var (client, recorder) = WithRecorder();

        var response = await client.GetAsync($"/api/doctors/search?text={doctor.LastName}&city=Ankara&gender=Female", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var e = Assert.Single(recorder.Events);
        Assert.Equal(DoctorActivityEvent.SearchType, e.Type);
        Assert.Equal(doctor.LastName, e.Text);
        Assert.Contains("city=Ankara", e.Filters);
        Assert.Contains("gender=Female", e.Filters);
        Assert.Equal(1, e.ResultCount);
        Assert.Equal("mongo", e.Engine);
        Assert.Null(e.DoctorId);
        Assert.NotEqual(Guid.Empty, e.EventId);
    }

    [Fact]
    public async Task Search_event_carries_no_user_identity_even_when_the_caller_is_logged_in()
    {
        var (client, recorder) = WithRecorder(HastaneRoles.Patient);

        await client.GetAsync("/api/doctors/search?text=kardiyoloji", Ct);

        var json = JsonSerializer.Serialize(Assert.Single(recorder.Events), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("user-1", json);
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "engine", "eventId", "filters", "occurredAt", "resultCount", "text", "type", "doctorId" }.Order().ToArray(), names);
    }

    [Fact]
    public async Task Public_profile_writes_a_profile_view_event_keyed_by_doctor_id()
    {
        var doctor = await CreateDoctorAsync();
        var (client, recorder) = WithRecorder();

        var response = await client.GetAsync($"/api/doctors/{doctor.Id}/public", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var e = Assert.Single(recorder.Events);
        Assert.Equal(DoctorActivityEvent.ProfileViewType, e.Type);
        Assert.Equal(doctor.Id, e.DoctorId);
    }

    [Fact]
    public async Task Authenticated_detail_also_writes_a_profile_view_event()
    {
        var doctor = await CreateDoctorAsync();
        var (client, recorder) = WithRecorder(HastaneRoles.Patient);

        var response = await client.GetAsync($"/api/doctors/{doctor.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(doctor.Id, Assert.Single(recorder.Events).DoctorId);
    }

    [Fact]
    public async Task Unknown_doctor_404_writes_no_event()
    {
        var (client, recorder) = WithRecorder();

        var response = await client.GetAsync("/api/doctors/64b7f0f0f0f0f0f0f0f0f0f0/public", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public async Task Invalid_search_400_writes_no_event()
    {
        var (client, recorder) = WithRecorder();

        var response = await client.GetAsync("/api/doctors/search?gender=Alien", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public void Every_event_gets_its_own_event_id()
    {
        var a = DoctorActivityEvent.ProfileView("d1");
        var b = DoctorActivityEvent.ProfileView("d1");

        Assert.NotEqual(a.EventId, b.EventId);
    }

    [Fact]
    public void Search_summary_lists_only_used_filters_and_clips_long_text()
    {
        var e = DoctorActivityEvent.Search(new DoctorSearchQuery { Text = new string('a', 100), MinFee = 100, MaxFee = 300, Sort = "name" }, 0, "mongo");

        Assert.Equal("fee=100-300", e.Filters);
        Assert.Equal(100, e.Text!.Length);
        Assert.Null(DoctorActivityEvent.Search(new DoctorSearchQuery(), 0, "mongo").Filters);
    }

    // ---------- Kafka kapalıyken istek bozulmaz ----------

    [Fact]
    public async Task With_kafka_off_search_and_profile_return_200_and_nothing_is_queued()
    {
        var doctor = await CreateDoctorAsync();
        var client = _factory.CreateClient();
        var publisher = _factory.Services.GetRequiredService<ChannelDoctorActivityPublisher>();

        var search = await client.GetAsync("/api/doctors/search?text=olay", Ct);
        var profile = await client.GetAsync($"/api/doctors/{doctor.Id}/public", Ct);

        Assert.False(publisher.Enabled);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal(0, publisher.DroppedCount);
        Assert.False(publisher.Reader.TryPeek(out _));
    }

    [Fact]
    public async Task With_kafka_configured_but_unreachable_requests_still_return_200_and_stay_fast()
    {
        var doctor = await CreateDoctorAsync();
        // 127.0.0.1:1 -> bağlantı hemen reddedilir; gerçek üretici arka planda yeniden dener, istek yolu bunu görmez.
        using var broken = _factory.WithWebHostBuilder(b => b.UseSetting("Kafka:BootstrapServers", "127.0.0.1:1"));
        var client = broken.CreateClient();
        await client.GetAsync("/api/doctors/search?text=isit", Ct); // ilk istek JIT/ısınma payı dışarıda

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/doctors/search?text=olay{i}", Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/doctors/{doctor.Id}/public", Ct)).StatusCode);
        }
        sw.Stop();

        // 40 istek: Kafka'yı beklemiyorsak saniyeler değil, ~yüzlerce ms. Bağlantı zaman aşımı (5 sn) olsa bu sınır aşılırdı.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), $"Kafka yokken istekler yavaşladı: {sw.Elapsed}");
    }

    [Fact]
    public async Task When_the_channel_is_full_requests_are_unaffected_and_extra_events_are_dropped()
    {
        // Üretici hizmeti çıkarılır: kanalı kimse boşaltmaz, kapasite (2) dolar.
        using var full = _factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Kafka:BootstrapServers", "127.0.0.1:1");
            b.UseSetting("Kafka:ChannelCapacity", "2");
            b.ConfigureServices(s =>
            {
                foreach (var d in s.Where(d => d.ImplementationType == typeof(DoctorActivityProducerService)).ToList())
                    s.Remove(d);
            });
        });
        var client = full.CreateClient();

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/doctors/search?text=dolu{i}", Ct)).StatusCode);

        var publisher = full.Services.GetRequiredService<ChannelDoctorActivityPublisher>();
        Assert.Equal(8, publisher.DroppedCount);
        Assert.True(publisher.Reader.TryRead(out var first));
        Assert.Equal("dolu0", first!.Text); // kanal DOLUYKEN gelenler düşer; en eskiler korunur
    }

    // ---------- kanal birim testi ----------

    private static ChannelDoctorActivityPublisher NewPublisher(string servers, int capacity)
        => new(Options.Create(new KafkaOptions { BootstrapServers = servers, ChannelCapacity = capacity }), NullLogger<ChannelDoctorActivityPublisher>.Instance);

    [Fact]
    public void Channel_accepts_up_to_capacity_then_drops_and_counts_without_blocking()
    {
        var publisher = NewPublisher("localhost:9092", 2);

        var results = Enumerable.Range(0, 5).Select(_ => publisher.TryPublish(DoctorActivityEvent.ProfileView("d"))).ToArray();

        Assert.Equal([true, true, false, false, false], results);
        Assert.Equal(3, publisher.DroppedCount);
    }

    [Fact]
    public void Channel_keeps_the_order_of_events()
    {
        var publisher = NewPublisher("localhost:9092", 10);
        var ids = Enumerable.Range(0, 5).Select(i => $"d{i}").ToArray();
        foreach (var id in ids) publisher.TryPublish(DoctorActivityEvent.ProfileView(id));

        var read = new List<string>();
        while (publisher.Reader.TryRead(out var e)) read.Add(e.DoctorId!);

        Assert.Equal(ids, read);
    }

    [Fact]
    public void Disabled_publisher_queues_nothing()
    {
        var publisher = NewPublisher("", 10);

        Assert.False(publisher.TryPublish(DoctorActivityEvent.ProfileView("d")));
        Assert.False(publisher.Enabled);
        Assert.False(publisher.Reader.TryPeek(out _));
        Assert.Equal(0, publisher.DroppedCount);
    }
}
