using Hastane.Analytics.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Hastane.Analytics.Tests;

// İşleyici doğrudan (Kafka'sız, yalnız gerçek Mongo): idempotency ve atlama kuralları.
[Collection(AnalyticsCollection.Name)]
public class ActivityProcessorTests(AnalyticsApiFactory factory) : AnalyticsTestBase(factory)
{
    private IActivityProcessor Processor => Factory.Services.GetRequiredService<IActivityProcessor>();

    private static ActivityEvent View(string doctorId, Guid? id = null, DateTimeOffset? at = null)
        => new() { EventId = id ?? Guid.NewGuid(), Type = ActivityEvent.ProfileViewType, DoctorId = doctorId, OccurredAt = at ?? DateTimeOffset.UtcNow };

    private static ActivityEvent Find(string? text)
        => new() { EventId = Guid.NewGuid(), Type = ActivityEvent.SearchType, Text = text, OccurredAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task Same_event_id_second_time_is_a_duplicate_and_changes_nothing()
    {
        var doctorId = NewId("doc");
        var id = Guid.NewGuid();

        var first = await Processor.ProcessAsync(View(doctorId, id), Ct);
        var second = await Processor.ProcessAsync(View(doctorId, id), Ct);

        Assert.Equal(ProcessResult.Applied, first);
        Assert.Equal(ProcessResult.Duplicate, second);
        Assert.Equal(1, (await TopViewedAsync(Admin(), 7, 100)).Single(d => d.DoctorId == doctorId).Views);
    }

    [Fact]
    public async Task Two_different_events_for_one_doctor_add_up()
    {
        var doctorId = NewId("doc");

        await Processor.ProcessAsync(View(doctorId), Ct);
        await Processor.ProcessAsync(View(doctorId), Ct);

        Assert.Equal(2, (await TopViewedAsync(Admin(), 7, 100)).Single(d => d.DoctorId == doctorId).Views);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("  a ")]
    [InlineData("ali@mail.com")]
    [InlineData("0532 123 45 67")]
    public async Task Empty_short_and_personal_looking_searches_are_ignored(string? text)
    {
        Assert.Equal(ProcessResult.Ignored, await Processor.ProcessAsync(Find(text), Ct));
    }

    [Fact]
    public async Task Ignored_events_leave_no_dedup_coupon_behind()
    {
        var before = await Factory.Database.GetCollection<MongoDB.Bson.BsonDocument>("processed_events").CountDocumentsAsync(FilterDefinition<MongoDB.Bson.BsonDocument>.Empty, cancellationToken: Ct);

        await Processor.ProcessAsync(Find("ab"), Ct);
        await Processor.ProcessAsync(new ActivityEvent { EventId = Guid.NewGuid(), Type = "unknown", OccurredAt = DateTimeOffset.UtcNow }, Ct);
        await Processor.ProcessAsync(new ActivityEvent { EventId = Guid.NewGuid(), Type = ActivityEvent.ProfileViewType, DoctorId = " ", OccurredAt = DateTimeOffset.UtcNow }, Ct);

        var after = await Factory.Database.GetCollection<MongoDB.Bson.BsonDocument>("processed_events").CountDocumentsAsync(FilterDefinition<MongoDB.Bson.BsonDocument>.Empty, cancellationToken: Ct);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Empty_event_id_is_ignored()
    {
        var e = View(NewId("doc"));
        e.EventId = Guid.Empty;

        Assert.Equal(ProcessResult.Ignored, await Processor.ProcessAsync(e, Ct));
    }

    [Fact]
    public async Task Dedup_coupon_collection_has_a_ttl_index()
    {
        using var cursor = await Factory.Database.GetCollection<MongoDB.Bson.BsonDocument>("processed_events").Indexes.ListAsync(Ct);
        var indexes = await cursor.ToListAsync(Ct);

        var ttl = Assert.Single(indexes, i => i.Contains("expireAfterSeconds"));
        Assert.Equal(8 * 24 * 3600, ttl["expireAfterSeconds"].ToInt32());
    }
}
