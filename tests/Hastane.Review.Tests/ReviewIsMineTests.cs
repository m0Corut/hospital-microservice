using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hastane.Review.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Hastane.Review.Tests;

// Gece N3b: yanıtlara isMine. Okuma uçları anonim, ama token varsa kimlik okunur.
// Karar: geçersiz token (süresi dolmuş, imza bozuk, alg none) bu uçlarda 401 DEĞİL, anonim gibi davranır (200, isMine=false).
public class ReviewIsMineTests : IClassFixture<ReviewApiFactory>
{
    private readonly ReviewApiFactory _factory;

    public ReviewIsMineTests(ReviewApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientWith(string? token)
    {
        var client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    private static string PatientToken(string userId) => TestJwt.Create(userId, [HastaneRoles.Patient], name: "Hasta " + userId);

    private async Task<ResultReviewDto> CreateAsync(string userId, string doctorId)
    {
        var response = await ClientWith(PatientToken(userId)).PostAsJsonAsync("/api/reviews",
            new CreateReviewDto { DoctorId = doctorId, Rating = 5, Comment = "x" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResultReviewDto>(Ct))!;
    }

    private static string NewDoctor() => "doc-mine-" + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task Create_Response_IsMineTrue()
    {
        var created = await CreateAsync("owner-1", NewDoctor());
        Assert.True(created.IsMine);
    }

    [Fact]
    public async Task GetById_IsMine_TrueForOwner_FalseForOthers_FalseForAnonymous_FalseForAdmin()
    {
        var created = await CreateAsync("owner-2", NewDoctor());
        var url = $"/api/reviews/{created.Id}";

        Assert.True((await ClientWith(PatientToken("owner-2")).GetFromJsonAsync<ResultReviewDto>(url, Ct))!.IsMine);
        Assert.False((await ClientWith(PatientToken("someone-else")).GetFromJsonAsync<ResultReviewDto>(url, Ct))!.IsMine);
        Assert.False((await ClientWith(null).GetFromJsonAsync<ResultReviewDto>(url, Ct))!.IsMine);
        Assert.False((await ClientWith(TestJwt.Create("admin-1", [HastaneRoles.Admin])).GetFromJsonAsync<ResultReviewDto>(url, Ct))!.IsMine);
    }

    [Fact]
    public async Task List_MarksOnlyOwnReviews()
    {
        var doctor = NewDoctor();
        var mine = await CreateAsync("list-me", doctor);
        var theirs = await CreateAsync("list-other", doctor);

        var asMe = (await ClientWith(PatientToken("list-me")).GetFromJsonAsync<List<ResultReviewDto>>($"/api/reviews?doctorId={doctor}", Ct))!;
        Assert.Equal(2, asMe.Count);
        Assert.True(asMe.Single(r => r.Id == mine.Id).IsMine);
        Assert.False(asMe.Single(r => r.Id == theirs.Id).IsMine);

        var anonymous = (await ClientWith(null).GetFromJsonAsync<List<ResultReviewDto>>($"/api/reviews?doctorId={doctor}", Ct))!;
        Assert.All(anonymous, r => Assert.False(r.IsMine));

        var all = (await ClientWith(PatientToken("list-me")).GetFromJsonAsync<List<ResultReviewDto>>("/api/reviews", Ct))!;
        Assert.True(all.Single(r => r.Id == mine.Id).IsMine);
    }

    [Fact]
    public async Task ReviewerId_NeverInResponse_ForAnyViewer()
    {
        var doctor = NewDoctor();
        var created = await CreateAsync("secret-owner-id", doctor);

        foreach (var token in new string?[] { null, PatientToken("secret-owner-id"), PatientToken("other"), TestJwt.Create("admin-1", [HastaneRoles.Admin]) })
        {
            var client = ClientWith(token);
            var one = await client.GetStringAsync($"/api/reviews/{created.Id}", Ct);
            var list = await client.GetStringAsync($"/api/reviews?doctorId={doctor}", Ct);
            foreach (var text in new[] { one, list })
            {
                Assert.DoesNotContain("reviewerId", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("secret-owner-id", text);
                Assert.Contains("\"isMine\"", text);
            }
        }

        // Create yanıtı da ReviewerId taşımaz.
        var createdJson = JsonSerializer.Serialize(created);
        Assert.DoesNotContain("secret-owner-id", createdJson);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("broken-signature")]
    [InlineData("alg-none")]
    [InlineData("hs256-public-key-as-secret")]
    public async Task InvalidToken_OnReadEndpoints_BehavesAsAnonymous_200_IsMineFalse(string kind)
    {
        var created = await CreateAsync("invalid-token-owner", NewDoctor());
        var roles = new[] { HastaneRoles.Patient };
        // Geçersiz token'ın içindeki sub, sahibin kimliğiyle AYNI: yine de "benim" sayılmamalı.
        var bad = kind switch
        {
            "expired" => TestJwt.CreateExpired("invalid-token-owner", roles),
            "broken-signature" => TestJwt.CreateWithBrokenSignature("invalid-token-owner", roles),
            "alg-none" => TestJwt.CreateAlgNone("invalid-token-owner", roles),
            _ => TestJwt.CreateHs256WithPublicKeyAsSecret("invalid-token-owner", roles),
        };

        var response = await ClientWith(bad).GetAsync($"/api/reviews/{created.Id}", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<ResultReviewDto>(Ct))!.IsMine);
    }

    [Fact]
    public async Task LegacyReview_WithoutReviewerId_IsNeverMine()
    {
        var client = new MongoClient(_factory.MongoConnectionString);
        var raw = client.GetDatabase("hastane_review_test").GetCollection<BsonDocument>("reviews");
        var id = ObjectId.GenerateNewId();
        await raw.InsertOneAsync(new BsonDocument
        {
            { "_id", id }, { "DoctorId", "doc-legacy-mine" }, { "ReviewerName", "Eski" },
            { "Rating", 3 }, { "CreatedAt", DateTime.UtcNow },
        }, cancellationToken: Ct);

        // Boş sub'lı çağıran bile eşleşmemeli; normal kullanıcı da.
        var dto = (await ClientWith(PatientToken("anyone")).GetFromJsonAsync<ResultReviewDto>($"/api/reviews/{id}", Ct))!;
        Assert.False(dto.IsMine);
    }

    [Fact]
    public async Task IsMine_DoesNotChangeWritePermissions()
    {
        var created = await CreateAsync("perm-owner", NewDoctor());
        // isMine gövdede gelse de yazma yetkisi/alanı etkilemez; başkasının yorumu hâlâ 404.
        var response = await ClientWith(PatientToken("perm-other")).PutAsJsonAsync($"/api/reviews/{created.Id}",
            new { id = created.Id, reviewerName = "X", rating = 1, isMine = true }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
