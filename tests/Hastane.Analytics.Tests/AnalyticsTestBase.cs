using System.Net.Http.Json;
using Hastane.Analytics.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hastane.Analytics.Tests;

public abstract class AnalyticsTestBase(AnalyticsApiFactory factory)
{
    protected AnalyticsApiFactory Factory { get; } = factory;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    protected static HttpClient AdminOf(WebApplicationFactory<Program> f)
        => f.CreateClient().WithBearer(TestJwt.Create("admin-1", [HastaneRoles.Admin]));

    protected HttpClient Admin() => AdminOf(Factory);

    // Doctor servisinin yazdığı olayların biçimi (camelCase JSON). Her olayın kendi benzersiz eventId'si var.
    protected static object ProfileView(string doctorId, Guid? eventId = null, DateTimeOffset? at = null) => new
    {
        eventId = eventId ?? Guid.NewGuid(),
        type = "profile_view",
        occurredAt = at ?? DateTimeOffset.UtcNow,
        doctorId,
    };

    protected static object Search(string text, Guid? eventId = null) => new
    {
        eventId = eventId ?? Guid.NewGuid(),
        type = "search",
        occurredAt = DateTimeOffset.UtcNow,
        text,
        filters = "city=Ankara",
        resultCount = 3,
        engine = "mongo",
    };

    /// <summary>Koşul sağlanana kadar yoklar (Kafka -> tüketici -> Mongo asenkron; sabit bekleme yerine sınırlı yoklama).</summary>
    protected static async Task EventuallyAsync(Func<Task<bool>> condition, string what, int seconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(250, Ct);
        }
        Assert.Fail($"Zaman aşımı ({seconds} sn): {what}");
    }

    protected async Task WaitForConsumerAsync()
        => await EventuallyAsync(async () => (await StatusAsync(Admin())).KafkaConnected, "tüketici konuya bağlanmadı", 90);

    protected static async Task<AnalyticsStatusDto> StatusAsync(HttpClient admin)
        => (await admin.GetFromJsonAsync<AnalyticsStatusDto>("/api/analytics/status", Ct))!;

    protected static async Task<List<TopViewedDoctorDto>> TopViewedAsync(HttpClient admin, int days = 7, int take = 100)
        => (await admin.GetFromJsonAsync<List<TopViewedDoctorDto>>($"/api/analytics/doctors/top-viewed?days={days}&take={take}", Ct))!;

    protected static async Task<List<TopSearchTermDto>> TopTermsAsync(HttpClient admin, int days = 7, int take = 100)
        => (await admin.GetFromJsonAsync<List<TopSearchTermDto>>($"/api/analytics/search/top-terms?days={days}&take={take}", Ct))!;
}
