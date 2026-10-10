using System.Net;
using static Hastane.Notification.Tests.NotificationTestHelpers;

namespace Hastane.Notification.Tests;

// Internal:ApiKey ayarı BOŞ iken iç uçlar HER ZAMAN 401: "anahtar tanımlı değil = herkese açık" olmaz,
// boş başlık gönderen de, rastgele başlık gönderen de geçemez.
public class InternalKeyNotConfiguredTests : IClassFixture<NoKeyNotificationApiFactory>
{
    private readonly NoKeyNotificationApiFactory _factory;

    public InternalKeyNotConfiguredTests(NoKeyNotificationApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/internal/notifications", null)]
    [InlineData("/api/internal/notifications", "")]
    [InlineData("/api/internal/notifications", "herhangi-bir-deger")]
    [InlineData("/api/internal/notifications", " ")]
    [InlineData("/api/internal/notifications/email", null)]
    [InlineData("/api/internal/notifications/email", "")]
    [InlineData("/api/internal/notifications/email", "herhangi-bir-deger")]
    public async Task WhenKeySettingIsEmpty_InternalEndpoints_Always401(string url, string? key)
    {
        var before = _factory.Email.Sent.Count;
        var body = url.EndsWith("/email", StringComparison.Ordinal) ? ValidEmail() : ValidNotification(userId: "u1");
        using var request = InternalRequest(url, body, key);

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, _factory.Email.Sent.Count);
    }
}
