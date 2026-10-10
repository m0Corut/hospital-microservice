using System.Net;
using System.Net.Http.Json;
using Hastane.Notification.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using MongoDB.Bson;
using MongoDB.Driver;
using static Hastane.Notification.Tests.NotificationTestHelpers;

namespace Hastane.Notification.Tests;

// İç uçlar: X-Internal-Key kapısı, girdi doğrulama, bildirim oluşturma, e-posta gönderimi ve günlük.
public class InternalEndpointsTests : IClassFixture<NotificationApiFactory>
{
    private const string NotificationsUrl = "/api/internal/notifications";
    private const string EmailUrl = "/api/internal/notifications/email";
    private const string Key = NotificationApiFactory.TestInternalKey;

    private readonly NotificationApiFactory _factory;

    public InternalEndpointsTests(NotificationApiFactory factory)
    {
        _factory = factory;
        _factory.Email.Failure = null;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IMongoCollection<BsonDocument> Raw(string collection)
        => new MongoClient(_factory.MongoConnectionString).GetDatabase(NotificationApiFactory.DatabaseName).GetCollection<BsonDocument>(collection);

    // ---- anahtar kapısı ----

    [Theory]
    [InlineData(NotificationsUrl)]
    [InlineData(EmailUrl)]
    public async Task InternalEndpoints_WithoutKey_Return401(string url)
    {
        var before = _factory.Email.Sent.Count;
        var body = url == EmailUrl ? ValidEmail() : ValidNotification(userId: "u1");
        using var request = InternalRequest(url, body, key: null);

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, _factory.Email.Sent.Count);
    }

    [Theory]
    [InlineData(NotificationsUrl, "yanlis-anahtar")]
    [InlineData(NotificationsUrl, "")]
    [InlineData(NotificationsUrl, Key + "x")]  // doğru anahtarın uzantısı
    [InlineData(NotificationsUrl, "test-internal-key-notificatio")] // doğru anahtarın öneki
    [InlineData(EmailUrl, "yanlis-anahtar")]
    [InlineData(EmailUrl, "TEST-INTERNAL-KEY-NOTIFICATION")] // büyük/küçük harf farkı
    public async Task InternalEndpoints_WithWrongKey_Return401(string url, string key)
    {
        var before = _factory.Email.Sent.Count;
        var body = url == EmailUrl ? ValidEmail() : ValidNotification(userId: "u1");
        using var request = InternalRequest(url, body, key);

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, _factory.Email.Sent.Count);
    }

    [Theory]
    [InlineData(NotificationsUrl)]
    [InlineData(EmailUrl)]
    public async Task InternalEndpoints_WithAdminJwtButNoKey_Return401(string url)
    {
        // JWT iç ucu açmaz: kapı yalnız anahtardır.
        var client = _factory.CreateClient().WithBearer(TestJwt.Create("admin-1", [HastaneRoles.Admin]));
        var body = url == EmailUrl ? ValidEmail() : ValidNotification(userId: "u1");
        using var request = InternalRequest(url, body, key: null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request, Ct)).StatusCode);
    }

    [Fact]
    public async Task InternalEndpoints_401_IsProblemDetails_WithoutKeyLeak()
    {
        using var request = InternalRequest(NotificationsUrl, ValidNotification(userId: "u1"), "yanlis");

        var response = await _factory.CreateClient().SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);

        Assert.Contains("json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(Key, text);
    }

    // ---- bildirim oluşturma ----

    [Fact]
    public async Task Create_ForUser_Returns201_AndStoresDocument()
    {
        var userId = Unique("usr");
        using var request = InternalRequest(NotificationsUrl, ValidNotification(userId: userId), Key);

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<ResultNotificationDto>(Json, Ct))!;
        Assert.False(string.IsNullOrEmpty(dto.Id));
        Assert.Equal("Başlık", dto.Title);
        Assert.Equal("appointment-created", dto.Kind);
        Assert.Null(dto.ReadAt);
        var raw = await Raw("notifications").Find(new BsonDocument("_id", ObjectId.Parse(dto.Id))).SingleAsync(Ct);
        Assert.Equal(userId, raw["UserId"].AsString);
        Assert.True(raw["DoctorId"].IsBsonNull || !raw.Contains("DoctorId"));
    }

    [Fact]
    public async Task Create_ForDoctor_Returns201_AddressedByDoctorId()
    {
        var doctorId = Unique("doc");
        using var request = InternalRequest(NotificationsUrl, ValidNotification(doctorId: doctorId), Key);

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<ResultNotificationDto>(Json, Ct))!;
        var raw = await Raw("notifications").Find(new BsonDocument("_id", ObjectId.Parse(dto.Id))).SingleAsync(Ct);
        Assert.Equal(doctorId, raw["DoctorId"].AsString);
    }

    [Fact]
    public async Task Create_WithoutKind_DefaultsToInfo_AndWithoutLinkIsAllowed()
    {
        using var request = InternalRequest(NotificationsUrl, new { userId = Unique("usr"), title = "t", message = "m" }, Key);

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<ResultNotificationDto>(Json, Ct))!;
        Assert.Equal("info", dto.Kind);
        Assert.Null(dto.Link);
    }

    [Fact]
    public async Task Create_SetsIndexesOnRecipientFields()
    {
        // İndeksler açılışta hosted service ile kurulur; kurulana kadar ilk isteğin gelmesini bekle.
        using var request = InternalRequest(NotificationsUrl, ValidNotification(userId: Unique("usr")), Key);
        await _factory.CreateClient().SendAsync(request, Ct);

        var indexes = await (await Raw("notifications").Indexes.ListAsync(Ct)).ToListAsync(Ct);
        var names = indexes.Select(i => i["name"].AsString).ToList();

        Assert.Contains(names, n => n.StartsWith("UserId_", StringComparison.Ordinal));
        Assert.Contains(names, n => n.StartsWith("DoctorId_", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("//evil.example.com")]
    [InlineData("/\\evil.example.com")]
    [InlineData("https://evil.example.com/x")]
    [InlineData("http://evil.example.com")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("Patient/Appointments/1")]   // "/" ile başlamıyor
    [InlineData("/ok\npath")]                 // satır sonu
    public async Task Create_WithUnsafeLink_Returns400_AndStoresNothing(string link)
    {
        var userId = Unique("usr");
        using var request = InternalRequest(NotificationsUrl, ValidNotification(userId: userId, link: link), Key);

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await Raw("notifications").CountDocumentsAsync(new BsonDocument("UserId", userId), cancellationToken: Ct));
    }

    [Theory]
    [InlineData("/Patient/Appointments/Details/abc")]
    [InlineData("/")]
    [InlineData("/a/b?x=1&y=2#frag")]
    public async Task Create_WithRelativeLink_Returns201(string link)
    {
        using var request = InternalRequest(NotificationsUrl, ValidNotification(userId: Unique("usr"), link: link), Key);

        Assert.Equal(HttpStatusCode.Created, (await _factory.CreateClient().SendAsync(request, Ct)).StatusCode);
    }

    [Fact]
    public async Task Create_WithTooLongFields_Returns400()
    {
        var client = _factory.CreateClient();
        var userId = Unique("usr");

        using var longTitle = InternalRequest(NotificationsUrl,
            new { userId, title = new string('a', 201), message = "m" }, Key);
        using var longMessage = InternalRequest(NotificationsUrl,
            new { userId, title = "t", message = new string('a', 1001) }, Key);
        using var okEdges = InternalRequest(NotificationsUrl,
            new { userId, title = new string('a', 200), message = new string('b', 1000) }, Key);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(longTitle, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(longMessage, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(okEdges, Ct)).StatusCode);
    }

    [Fact]
    public async Task Create_WithMissingTitleOrMessage_Returns400()
    {
        var client = _factory.CreateClient();
        using var noTitle = InternalRequest(NotificationsUrl, new { userId = "u", message = "m" }, Key);
        using var noMessage = InternalRequest(NotificationsUrl, new { userId = "u", title = "t" }, Key);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noTitle, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noMessage, Ct)).StatusCode);
    }

    [Fact]
    public async Task Create_NeedsExactlyOneRecipient()
    {
        var client = _factory.CreateClient();
        using var none = InternalRequest(NotificationsUrl, ValidNotification(), Key);
        using var both = InternalRequest(NotificationsUrl, ValidNotification(userId: "u", doctorId: "d"), Key);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(none, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(both, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("BÜYÜK")]
    [InlineData("has space")]
    [InlineData("kind\n")]
    public async Task Create_WithInvalidKind_Returns400(string kind)
    {
        using var request = InternalRequest(NotificationsUrl,
            new { userId = "u", title = "t", message = "m", kind }, Key);

        Assert.Equal(HttpStatusCode.BadRequest, (await _factory.CreateClient().SendAsync(request, Ct)).StatusCode);
    }

    // ---- e-posta ----

    [Fact]
    public async Task Email_Returns202_SendsThroughSender_AndLogsWithoutBody()
    {
        var to = $"{Unique("hasta")}@example.com";
        const string secretLink = "https://hastane.local/reset?token=COK-GIZLI-TOKEN-123";
        using var request = InternalRequest(EmailUrl, new
        {
            to,
            subject = "Şifre sıfırlama",
            textBody = $"Bağlantı: {secretLink}",
            htmlBody = $"<a href=\"{secretLink}\">sıfırla</a>",
        }, Key);

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var sent = Assert.Single(_factory.Email.Sent, m => m.To == to);
        Assert.Equal("Şifre sıfırlama", sent.Subject);
        Assert.Contains(secretLink, sent.TextBody);

        var log = await Raw("email_logs").Find(new BsonDocument("To", to)).SingleAsync(Ct);
        Assert.True(log["Success"].AsBoolean);
        Assert.Equal("Şifre sıfırlama", log["Subject"].AsString);
        Assert.True(log.Contains("SentAt"));
        // Gövde günlüğe GİRMEZ: ne alan olarak ne de metin olarak.
        Assert.False(log.Contains("TextBody"));
        Assert.False(log.Contains("HtmlBody"));
        Assert.DoesNotContain("COK-GIZLI-TOKEN-123", log.ToJson());
    }

    [Fact]
    public async Task Email_WhenSmtpFails_Returns502_AndLogsFailure()
    {
        var to = $"{Unique("hasta")}@example.com";
        _factory.Email.Failure = new InvalidOperationException("SMTP kapalı (test)");
        using var request = InternalRequest(EmailUrl, new { to, subject = "Konu", textBody = "gizli gövde" }, Key);

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var log = await Raw("email_logs").Find(new BsonDocument("To", to)).SingleAsync(Ct);
        Assert.False(log["Success"].AsBoolean);
        Assert.Contains("InvalidOperationException", log["Error"].AsString);
        Assert.False(log.Contains("TextBody"));
        Assert.DoesNotContain("gizli gövde", log.ToJson());
    }

    [Fact]
    public async Task Email_ErrorInLog_IsTruncatedTo300Characters()
    {
        var to = $"{Unique("hasta")}@example.com";
        _factory.Email.Failure = new InvalidOperationException(new string('x', 2000));
        using var request = InternalRequest(EmailUrl, new { to, subject = "Konu", textBody = "g" }, Key);

        await _factory.CreateClient().SendAsync(request, Ct);

        var log = await Raw("email_logs").Find(new BsonDocument("To", to)).SingleAsync(Ct);
        Assert.Equal(300, log["Error"].AsString.Length);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    [InlineData("@b.com")]
    [InlineData("Ali <ali@example.com>")]
    [InlineData("a@b.com, c@d.com")]
    [InlineData("a@b.com\r\nBcc: kurban@example.com")]
    [InlineData("")]
    public async Task Email_WithInvalidAddress_Returns400_AndNothingIsSent(string to)
    {
        var before = _factory.Email.Sent.Count;
        using var request = InternalRequest(EmailUrl, new { to, subject = "Konu", textBody = "g" }, Key);

        var response = await _factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, _factory.Email.Sent.Count);
    }

    [Fact]
    public async Task Email_WithBadSubjectOrBody_Returns400()
    {
        var client = _factory.CreateClient();
        var before = _factory.Email.Sent.Count;
        using var newlineSubject = InternalRequest(EmailUrl,
            new { to = "a@b.com", subject = "Konu\r\nBcc: x@y.com", textBody = "g" }, Key);
        using var trailingNewline = InternalRequest(EmailUrl,
            new { to = "a@b.com", subject = "Konu\n", textBody = "g" }, Key);
        using var longSubject = InternalRequest(EmailUrl,
            new { to = "a@b.com", subject = new string('a', 201), textBody = "g" }, Key);
        using var noBody = InternalRequest(EmailUrl, new { to = "a@b.com", subject = "Konu" }, Key);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(newlineSubject, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(trailingNewline, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(longSubject, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noBody, Ct)).StatusCode);
        Assert.Equal(before, _factory.Email.Sent.Count);
    }
}
