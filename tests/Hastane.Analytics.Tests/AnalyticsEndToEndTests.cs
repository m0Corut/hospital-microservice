using System.Net;
using System.Net.Http.Json;
using Hastane.Analytics.Api.Dtos;
using MongoDB.Bson;

namespace Hastane.Analytics.Tests;

// Uçtan uca: gerçek Kafka'ya olay yaz -> arka plandaki tüketici okur -> Mongo toplamları -> Admin uçları.
[Collection(AnalyticsCollection.Name)]
public class AnalyticsEndToEndTests(AnalyticsApiFactory factory) : AnalyticsTestBase(factory)
{
    [Fact]
    public async Task Profile_view_events_become_a_daily_total_for_that_doctor()
    {
        await WaitForConsumerAsync();
        var doctorId = NewId("doc");
        // Anahtar = doctorId: aynı doktorun olayları aynı partition'da.
        await Factory.ProduceAsync((doctorId, ProfileView(doctorId)), (doctorId, ProfileView(doctorId)), (doctorId, ProfileView(doctorId)));

        await EventuallyAsync(async () => (await TopViewedAsync(Admin())).Any(d => d.DoctorId == doctorId && d.Views == 3),
            "3 görüntüleme toplama yansımadı");
    }

    [Fact]
    public async Task Search_events_are_counted_per_normalized_term()
    {
        await WaitForConsumerAsync();
        var tag = "kalp" + Guid.NewGuid().ToString("N")[..6]; // benzersiz terim
        // "KARDİYOLOJİ", "Kardiyoloji " ve küçük harf aynı terim: küçük harf + Türkçe katlama
        await Factory.ProduceAsync((null, Search(tag.ToUpperInvariant() + " ÇOCUK")), (null, Search(" " + tag + " Çocuk ")), (null, Search(tag + " cocuk")));

        await EventuallyAsync(async () => (await TopTermsAsync(Admin())).Any(t => t.Term == $"{tag} cocuk" && t.Count == 3),
            "normalize edilmiş terim 3 kez sayılmadı");
    }

    [Fact]
    public async Task The_same_event_id_delivered_twice_is_counted_once()
    {
        await WaitForConsumerAsync();
        var doctorId = NewId("doc");
        var duplicate = Guid.NewGuid();
        // E1, E1 (tekrar), E2: aynı anahtar = aynı partition = sıralı. E2 sayıldıysa E1'in tekrarı da çoktan işlenmiştir.
        await Factory.ProduceAsync(
            (doctorId, ProfileView(doctorId, duplicate)),
            (doctorId, ProfileView(doctorId, duplicate)),
            (doctorId, ProfileView(doctorId)));

        await EventuallyAsync(async () => (await TopViewedAsync(Admin())).Any(d => d.DoctorId == doctorId && d.Views >= 2),
            "olaylar işlenmedi");
        var views = (await TopViewedAsync(Admin())).Single(d => d.DoctorId == doctorId).Views;
        Assert.Equal(2, views); // 3 mesaj geldi, 2 benzersiz olay
    }

    [Fact]
    public async Task Offsets_are_committed_only_after_events_are_processed()
    {
        await WaitForConsumerAsync();
        var doctorId = NewId("doc");
        await Factory.ProduceAsync((doctorId, ProfileView(doctorId)), (doctorId, ProfileView(doctorId)));

        await EventuallyAsync(async () => (await TopViewedAsync(Admin())).Any(d => d.DoctorId == doctorId && d.Views == 2), "olaylar işlenmedi");

        // Grubun commit ettiği offset toplamı = konuya yazılan mesaj sayısı: işlenen her mesaj commit edildi (otomatik commit kapalı).
        await EventuallyAsync(() => Task.FromResult(Factory.CommittedOffsetTotal() == Factory.ProducedCount),
            "commit edilen offset'ler üretilen mesaj sayısına ulaşmadı");
    }

    [Fact]
    public async Task Status_reports_connection_and_processed_counts()
    {
        await WaitForConsumerAsync();
        var before = await StatusAsync(Admin());
        var doctorId = NewId("doc");
        await Factory.ProduceAsync((doctorId, ProfileView(doctorId)));

        await EventuallyAsync(async () => (await StatusAsync(Admin())).ProcessedToday >= before.ProcessedToday + 1, "bugün işlenen sayısı artmadı");

        var status = await StatusAsync(Admin());
        Assert.True(status.KafkaEnabled);
        Assert.True(status.KafkaConnected);
        Assert.Equal(Factory.Topic, status.Topic);
        Assert.Equal(Factory.GroupId, status.GroupId);
        Assert.NotNull(status.LastProcessedAt);
        Assert.True(DateTime.UtcNow - status.LastProcessedAt < TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Rebuild_deletes_the_totals_rewinds_the_group_and_recomputes_the_same_numbers_from_the_log()
    {
        await WaitForConsumerAsync();
        var admin = Admin();
        var doctorId = NewId("doc");
        var tag = "yeniden" + Guid.NewGuid().ToString("N")[..6];
        await Factory.ProduceAsync(
            (doctorId, ProfileView(doctorId)), (doctorId, ProfileView(doctorId)), (doctorId, ProfileView(doctorId)),
            (null, Search(tag + " arama")), (null, Search(tag + " arama")));
        await EventuallyAsync(async () => (await TopViewedAsync(admin)).Any(d => d.DoctorId == doctorId && d.Views == 3)
                                          && (await TopTermsAsync(admin)).Any(t => t.Term == tag + " arama" && t.Count == 2), "olaylar işlenmedi");

        // Kafka'dan gelmeyen sahte toplam: yeniden hesaplamada kaybolmalı (toplamlar logdan kurulur, log dışı bir şey yaşamaz).
        var ghost = NewId("ghost");
        await Factory.Database.GetCollection<BsonDocument>("doctor_views_daily").InsertOneAsync(new BsonDocument
        {
            { "_id", $"{ghost}|{DateTime.UtcNow.AddHours(3):yyyy-MM-dd}" }, { "DoctorId", ghost },
            { "Day", DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd") }, { "Count", 99L },
        }, cancellationToken: Ct);
        Assert.Contains(await TopViewedAsync(admin), d => d.DoctorId == ghost);
        var viewsBefore = (await TopViewedAsync(admin)).Where(d => d.DoctorId != ghost).OrderBy(d => d.DoctorId).Select(d => (d.DoctorId, d.Views)).ToList();
        var termsBefore = (await TopTermsAsync(admin)).OrderBy(t => t.Term).Select(t => (t.Term, t.Count)).ToList();

        var response = await admin.PostAsync("/api/analytics/rebuild", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<RebuildResultDto>(Ct))!;
        Assert.Equal(int.Parse(AnalyticsApiFactory.Partitions), result.PartitionsRewound);
        Assert.True(result.AggregatesDeleted > 0);

        await EventuallyAsync(async () =>
        {
            var views = (await TopViewedAsync(admin)).OrderBy(d => d.DoctorId).Select(d => (d.DoctorId, d.Views)).ToList();
            var terms = (await TopTermsAsync(admin)).OrderBy(t => t.Term).Select(t => (t.Term, t.Count)).ToList();
            return views.SequenceEqual(viewsBefore) && terms.SequenceEqual(termsBefore);
        }, "yeniden hesaplanan toplamlar öncekiyle aynı olmadı");
        Assert.DoesNotContain(await TopViewedAsync(admin), d => d.DoctorId == ghost);
    }

    [Fact]
    public async Task Old_events_are_outside_a_short_window_but_inside_a_long_one()
    {
        await WaitForConsumerAsync();
        var doctorId = NewId("doc");
        await Factory.ProduceAsync((doctorId, ProfileView(doctorId, at: DateTimeOffset.UtcNow.AddDays(-30))));

        await EventuallyAsync(async () => (await TopViewedAsync(Admin(), days: 90)).Any(d => d.DoctorId == doctorId), "eski olay işlenmedi");
        Assert.DoesNotContain(await TopViewedAsync(Admin(), days: 7), d => d.DoctorId == doctorId);
    }

    [Theory]
    [InlineData("/api/analytics/doctors/top-viewed?days=0")]
    [InlineData("/api/analytics/doctors/top-viewed?days=91")]
    [InlineData("/api/analytics/doctors/top-viewed?take=0")]
    [InlineData("/api/analytics/search/top-terms?take=101")]
    public async Task Out_of_range_query_values_are_a_400(string url)
    {
        using var kafkaOff = Factory.WithKafka("");

        var response = await AdminOf(kafkaOff).GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
