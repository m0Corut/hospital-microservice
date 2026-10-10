using System.Net;
using System.Net.Http.Json;
using Hastane.Notification.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using MongoDB.Bson;
using MongoDB.Driver;
using static Hastane.Notification.Tests.NotificationTestHelpers;

namespace Hastane.Notification.Tests;

// GET /api/notifications/me, POST {id}/read, POST read-all: alıcı token'dan (sub ya da doctor_id), başkasının bildirimine dokunulamaz.
public class MyNotificationsTests : IClassFixture<NotificationApiFactory>
{
    private readonly NotificationApiFactory _factory;

    public MyNotificationsTests(NotificationApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Anonymous() => _factory.CreateClient();
    private HttpClient PatientClient(string sub) => Anonymous().WithBearer(TestJwt.Create(sub, [HastaneRoles.Patient]));
    private HttpClient DoctorClient(string sub, string doctorId) => Anonymous().WithBearer(TestJwt.Create(sub, [HastaneRoles.Doctor], doctorId));

    private Task<ResultNotificationDto> Make(string? userId, string? doctorId, string title = "Başlık")
        => CreateNotificationAsync(Anonymous(), userId, doctorId, title, Ct);

    private static async Task<NotificationPageDto> Me(HttpClient client, string query = "")
    {
        var response = await client.GetAsync("/api/notifications/me" + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NotificationPageDto>(Json, Ct))!;
    }

    // ---- kimlik ----

    [Fact]
    public async Task AllEndpoints_WithoutToken_Return401()
    {
        var anonymous = Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/notifications/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/notifications/64b7f0f0f0f0f0f0f0f0f0f0/read", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/notifications/read-all", null, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("broken-signature")]
    [InlineData("alg-none")]
    public async Task Me_WithInvalidToken_Returns401(string kind)
    {
        var roles = new[] { HastaneRoles.Patient };
        var token = kind switch
        {
            "expired" => TestJwt.CreateExpired("u1", roles),
            "broken-signature" => TestJwt.CreateWithBrokenSignature("u1", roles),
            _ => TestJwt.CreateAlgNone("u1", roles),
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous().WithBearer(token).GetAsync("/api/notifications/me", Ct)).StatusCode);
    }

    [Theory]
    [InlineData(HastaneRoles.Patient)]
    [InlineData(HastaneRoles.Doctor)]
    [InlineData(HastaneRoles.Admin)]
    public async Task Me_IsOpenToAnyAuthenticatedRole(string role)
    {
        var client = Anonymous().WithBearer(TestJwt.Create(Unique("usr"), [role]));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/notifications/me", Ct)).StatusCode);
    }

    // ---- alıcı: sub ya da doctor_id ----

    [Fact]
    public async Task Me_BySub_ReturnsOnlyOwnNotifications()
    {
        var mine = Unique("usr");
        var other = Unique("usr");
        var created = await Make(mine, null, "Benim");
        await Make(other, null, "Başkasının");

        var page = await Me(PatientClient(mine));

        var item = Assert.Single(page.Items);
        Assert.Equal(created.Id, item.Id);
        Assert.Equal("Benim", item.Title);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Me_ByDoctorId_ReturnsDoctorAddressedNotifications_AndUserAddressedOnes()
    {
        var sub = Unique("usr");
        var doctorId = Unique("doc");
        var byDoctor = await Make(null, doctorId, "Doktora");
        var bySub = await Make(sub, null, "Kullanıcıya");
        await Make(null, Unique("doc"), "Başka doktora");

        var page = await Me(DoctorClient(sub, doctorId));

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(new[] { byDoctor.Id, bySub.Id }.Order(), page.Items.Select(i => i.Id).Order());
    }

    [Fact]
    public async Task Me_PatientWithSameStringAsDoctorId_DoesNotSeeDoctorNotifications()
    {
        // Hastanın sub'ı doktor kimliğiyle aynı metin olsa bile doktor bildirimi görünmez: doctor_id claim'i yoksa
        // yalnız UserId alanı eşleşir, DoctorId alanı sub ile karşılaştırılmaz.
        var id = Unique("ayni");
        await Make(null, id, "Doktora");

        var page = await Me(PatientClient(id));

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Me_Response_DoesNotExposeRecipientIds()
    {
        var sub = Unique("usr");
        await Make(sub, null);

        var json = await PatientClient(sub).GetStringAsync("/api/notifications/me", Ct);

        Assert.DoesNotContain(sub, json);
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("doctorId", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- sıralama, sayfalama ----

    [Fact]
    public async Task Me_IsNewestFirst_AndPaginates()
    {
        var sub = Unique("usr");
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await Make(sub, null, $"n{i}")).Id);
            await Task.Delay(5, Ct); // CreatedAt milisaniye hassasiyetinde: sıra net olsun
        }
        var client = PatientClient(sub);

        var first = await Me(client, "?page=1&pageSize=2");
        var second = await Me(client, "?page=2&pageSize=2");
        var third = await Me(client, "?page=3&pageSize=2");

        Assert.Equal(5, first.TotalCount);
        Assert.Equal([ids[4], ids[3]], first.Items.Select(i => i.Id));
        Assert.Equal([ids[2], ids[1]], second.Items.Select(i => i.Id));
        Assert.Equal([ids[0]], third.Items.Select(i => i.Id));
        Assert.Equal(2, second.Page);
        Assert.Equal(2, second.PageSize);
    }

    [Theory]
    [InlineData("?pageSize=1000", 1, 100)]
    [InlineData("?pageSize=101", 1, 100)]
    [InlineData("?pageSize=0", 1, 20)]
    [InlineData("?pageSize=-5", 1, 20)]
    [InlineData("?page=0", 1, 20)]
    [InlineData("?page=-3", 1, 20)]
    [InlineData("", 1, 20)]
    public async Task Me_ClampsPageAndPageSize(string query, int expectedPage, int expectedPageSize)
    {
        var page = await Me(PatientClient(Unique("usr")), query);

        Assert.Equal(expectedPage, page.Page);
        Assert.Equal(expectedPageSize, page.PageSize);
    }

    [Fact]
    public async Task Me_PageSizeCap_LimitsReturnedItems()
    {
        var sub = Unique("usr");
        for (var i = 0; i < 105; i++) await Make(sub, null, $"n{i}");

        var page = await Me(PatientClient(sub), "?pageSize=1000");

        Assert.Equal(100, page.PageSize);
        Assert.Equal(100, page.Items.Count);
        Assert.Equal(105, page.TotalCount);
    }

    // ---- okunmamış sayısı, okundu işaretleme ----

    [Fact]
    public async Task UnreadCount_AndUnreadOnly_FollowRead()
    {
        var sub = Unique("usr");
        var a = await Make(sub, null, "a");
        await Make(sub, null, "b");
        await Make(sub, null, "c");
        var client = PatientClient(sub);
        Assert.Equal(3, (await Me(client)).UnreadCount);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/notifications/{a.Id}/read", null, Ct)).StatusCode);

        var all = await Me(client);
        Assert.Equal(2, all.UnreadCount);
        Assert.Equal(3, all.TotalCount);
        Assert.NotNull(all.Items.Single(i => i.Id == a.Id).ReadAt);

        var unread = await Me(client, "?unreadOnly=true");
        Assert.Equal(2, unread.TotalCount);
        Assert.Equal(2, unread.UnreadCount);
        Assert.DoesNotContain(unread.Items, i => i.Id == a.Id);
    }

    [Fact]
    public async Task Read_AlreadyRead_Returns204_AndKeepsFirstReadAt()
    {
        var sub = Unique("usr");
        var n = await Make(sub, null);
        var client = PatientClient(sub);
        await client.PostAsync($"/api/notifications/{n.Id}/read", null, Ct);
        var firstReadAt = (await Me(client)).Items.Single().ReadAt;
        await Task.Delay(20, Ct);

        var again = await client.PostAsync($"/api/notifications/{n.Id}/read", null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(firstReadAt, (await Me(client)).Items.Single().ReadAt);
    }

    [Fact]
    public async Task Read_OthersNotification_Returns404_AndStaysUnread()
    {
        var owner = Unique("usr");
        var n = await Make(owner, null);

        var response = await PatientClient(Unique("usr")).PostAsync($"/api/notifications/{n.Id}/read", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var raw = await new MongoClient(_factory.MongoConnectionString).GetDatabase(NotificationApiFactory.DatabaseName)
            .GetCollection<BsonDocument>("notifications").Find(new BsonDocument("_id", ObjectId.Parse(n.Id))).SingleAsync(Ct);
        Assert.True(raw["ReadAt"].IsBsonNull);
    }

    [Fact]
    public async Task Read_DoctorNotification_ByOtherDoctor_Returns404_ByOwnDoctor_Returns204()
    {
        var doctorId = Unique("doc");
        var n = await Make(null, doctorId);

        Assert.Equal(HttpStatusCode.NotFound,
            (await DoctorClient(Unique("usr"), Unique("doc")).PostAsync($"/api/notifications/{n.Id}/read", null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await DoctorClient(Unique("usr"), doctorId).PostAsync($"/api/notifications/{n.Id}/read", null, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("64b7f0f0f0f0f0f0f0f0f0f0")] // biçim geçerli ama yok
    [InlineData("not-an-object-id")]
    [InlineData("123")]
    public async Task Read_UnknownOrMalformedId_Returns404(string id)
    {
        var response = await PatientClient(Unique("usr")).PostAsync($"/api/notifications/{id}/read", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReadAll_MarksOnlyMine()
    {
        var mine = Unique("usr");
        var other = Unique("usr");
        await Make(mine, null);
        await Make(mine, null);
        await Make(other, null);
        var client = PatientClient(mine);

        var response = await client.PostAsync("/api/notifications/read-all", null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, (await Me(client)).UnreadCount);
        Assert.Equal(1, (await Me(PatientClient(other))).UnreadCount);
    }

    [Fact]
    public async Task ReadAll_ForDoctor_CoversDoctorAddressedAndUserAddressed()
    {
        var sub = Unique("usr");
        var doctorId = Unique("doc");
        await Make(null, doctorId);
        await Make(sub, null);
        var client = DoctorClient(sub, doctorId);

        await client.PostAsync("/api/notifications/read-all", null, Ct);

        Assert.Equal(0, (await Me(client)).UnreadCount);
    }

    [Fact]
    public async Task ReadAll_WithNothingToRead_Returns204()
    {
        var response = await PatientClient(Unique("usr")).PostAsync("/api/notifications/read-all", null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
