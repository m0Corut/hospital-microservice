using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hastane.Analytics.Tests;

// Kafka kapalıyken servis ayakta kalır: uçlar Mongo'daki mevcut toplamları sunmaya devam eder, tüketici sessizce bekler.
[Collection(AnalyticsCollection.Name)]
public class AnalyticsKafkaDownTests(AnalyticsApiFactory factory) : AnalyticsTestBase(factory)
{
    [Fact]
    public async Task Kafka_not_configured_service_is_up_status_says_disabled_and_rebuild_is_409()
    {
        using var host = Factory.WithKafka("");
        var admin = AdminOf(host);

        var status = await StatusAsync(admin);
        var rebuild = await admin.PostAsync("/api/analytics/rebuild", null, Ct);

        Assert.False(status.KafkaEnabled);
        Assert.False(status.KafkaConnected);
        Assert.Equal(HttpStatusCode.Conflict, rebuild.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/analytics/doctors/top-viewed", Ct)).StatusCode);
    }

    [Fact]
    public async Task Kafka_configured_but_unreachable_service_stays_up_and_reports_not_connected()
    {
        // 127.0.0.1:1 -> hiçbir şey dinlemiyor. Tüketici yeniden dener; HTTP uçları etkilenmez.
        using var host = Factory.WithKafka("127.0.0.1:1");
        var admin = AdminOf(host);

        await Task.Delay(TimeSpan.FromSeconds(3), Ct); // tüketici birkaç tur denesin

        var status = await StatusAsync(admin);
        Assert.True(status.KafkaEnabled);
        Assert.False(status.KafkaConnected);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/analytics/search/top-terms", Ct)).StatusCode);
        var rebuild = await admin.PostAsync("/api/analytics/rebuild", null, Ct);
        Assert.Equal(HttpStatusCode.Conflict, rebuild.StatusCode);
    }
}
