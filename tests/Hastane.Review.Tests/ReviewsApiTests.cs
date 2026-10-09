using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hastane.Review.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Review.Tests;

public class ReviewsApiTests : IClassFixture<ReviewApiFactory>
{
    private readonly ReviewApiFactory _factory;
    private readonly HttpClient _client;

    // _client = Admin (okuma + herkesin yorumunu değiştirme/silme). Yorumu yazan Patient'tır: PatientClient.
    // Yetki ve sahiplik testleri ReviewAuthorizationTests'te.
    public ReviewsApiTests(ReviewApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient().WithBearer(TestJwt.Create("admin-1", [HastaneRoles.Admin]));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Görünen adı reviewer olan bir Patient: token "name" claim'i yorumun ReviewerName'i olur.
    private HttpClient PatientClient(string reviewer)
        => _factory.CreateClient().WithBearer(TestJwt.Create($"patient-{reviewer}", [HastaneRoles.Patient], name: reviewer));

    private static CreateReviewDto NewReview(string doctorId, int rating = 5) => new()
    {
        DoctorId = doctorId,
        Rating = rating,
        Comment = "Yorum",
    };

    private async Task<ResultReviewDto> CreateAsync(CreateReviewDto dto, string reviewer = "Ali")
    {
        var response = await PatientClient(reviewer).PostAsJsonAsync("/api/reviews", dto, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<ResultReviewDto>(Ct))!;
    }

    [Fact]
    public async Task Create_ThenList_ContainsReview()
    {
        var created = await CreateAsync(NewReview("doc-list", 4), "Veli");
        var list = await _client.GetFromJsonAsync<List<ResultReviewDto>>("/api/reviews", Ct);
        var item = Assert.Single(list!, r => r.Id == created.Id);
        Assert.Equal("Veli", item.ReviewerName);
        Assert.Equal(4, item.Rating);
        Assert.Equal("doc-list", item.DoctorId);
    }

    [Fact]
    public async Task Create_AssignsCreatedAtOnServer_IgnoringClientValue()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        // İstemci sahte bir CreatedAt göndermeye çalışır; DTO'da bu alan yok, gövdedeki fazlalık yok sayılır.
        var body = """{"doctorId":"doc-time","rating":5,"createdAt":"2000-01-01T00:00:00Z"}""";
        var response = await PatientClient("Ali").PostAsync("/api/reviews",
            new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResultReviewDto>(Ct))!;
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(created.CreatedAt, before, after);
        var fetched = await _client.GetFromJsonAsync<ResultReviewDto>($"/api/reviews/{created.Id}", Ct);
        Assert.InRange(fetched!.CreatedAt, before, after);
    }

    [Fact]
    public async Task List_FiltersByDoctorId_NewestFirst()
    {
        var first = await CreateAsync(NewReview("doc-sort-x"), "Birinci");
        await Task.Delay(20, Ct);
        var second = await CreateAsync(NewReview("doc-sort-x"), "İkinci");
        await CreateAsync(NewReview("doc-sort-y"));

        var list = await _client.GetFromJsonAsync<List<ResultReviewDto>>("/api/reviews?doctorId=doc-sort-x", Ct);
        Assert.Equal([second.Id, first.Id], list!.Select(r => r.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Create_WithRatingOutOfRange_Returns400(int rating)
    {
        var response = await PatientClient("Ali").PostAsJsonAsync("/api/reviews", NewReview("doc-bad", rating), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithTooLongComment_Returns400()
    {
        var dto = NewReview("doc-bad");
        dto.Comment = new string('a', 1001);
        Assert.Equal(HttpStatusCode.BadRequest, (await PatientClient("Ali").PostAsJsonAsync("/api/reviews", dto, Ct)).StatusCode);
    }

    [Fact]
    public async Task Update_ChangesFields_ButKeepsDoctorIdAndCreatedAt()
    {
        var created = await CreateAsync(NewReview("doc-upd", 2), "Eski");
        // DTO'da olmayan doctorId ve createdAt fazladan gönderilir; yok sayılmalı.
        var update = new
        {
            id = created.Id,
            doctorId = "baska-doktor",
            createdAt = "2000-01-01T00:00:00Z",
            reviewerName = "Yeni",   // DTO'da yok: yok sayılır, yorum sahibinin adı değişmez
            reviewerId = "baska-kullanici",
            rating = 5,
            comment = "Güncel",
        };
        var put = await _client.PutAsJsonAsync($"/api/reviews/{created.Id}", update, Ct);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var dto = await _client.GetFromJsonAsync<ResultReviewDto>($"/api/reviews/{created.Id}", Ct);
        Assert.Equal("Eski", dto!.ReviewerName);
        Assert.Equal(5, dto.Rating);
        Assert.Equal("Güncel", dto.Comment);
        Assert.Equal("doc-upd", dto.DoctorId);
        Assert.Equal(created.CreatedAt, dto.CreatedAt);
    }

    [Fact]
    public async Task Delete_ThenGet_Returns404()
    {
        var created = await CreateAsync(NewReview("doc-del"));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/reviews/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/reviews/{created.Id}", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("64b7f0f0f0f0f0f0f0f0f0f0")]
    [InlineData("gecersiz-id")]
    public async Task UnknownOrMalformedId_Returns404(string id)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/reviews/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/reviews/{id}", Ct)).StatusCode);
        var put = await _client.PutAsJsonAsync($"/api/reviews/{id}",
            new UpdateReviewDto { Id = id, Rating = 3 }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Put_WithMismatchedId_Returns400()
    {
        var created = await CreateAsync(NewReview("doc-mismatch"));
        var put = await _client.PutAsJsonAsync($"/api/reviews/{created.Id}",
            new UpdateReviewDto { Id = "64b7f0f0f0f0f0f0f0f0f0f0", Rating = 3 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Summary_WithThreeReviews_ReturnsAverageAndCount()
    {
        foreach (var rating in new[] { 5, 4, 3 })
            await CreateAsync(NewReview("doc-sum", rating));
        await CreateAsync(NewReview("doc-sum-other", 1));

        var json = await _client.GetStringAsync("/api/reviews/doctors/doc-sum/summary", Ct);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("doc-sum", doc.RootElement.GetProperty("doctorId").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("reviewCount").GetInt32());
        Assert.Equal(4.0, doc.RootElement.GetProperty("averageRating").GetDouble());
    }

    [Fact]
    public async Task Summary_RoundsToOneDecimal()
    {
        foreach (var rating in new[] { 5, 4, 4 })
            await CreateAsync(NewReview("doc-round", rating));
        var summary = await _client.GetFromJsonAsync<DoctorRatingSummaryDto>("/api/reviews/doctors/doc-round/summary", Ct);
        Assert.Equal(4.3, summary!.AverageRating);
    }

    [Fact]
    public async Task Summary_RoundsExactHalfUp()
    {
        // 5+4+4+4 = 17 / 4 = 4.25 tam yarım: okul yuvarlaması 4.3 verir, banker yuvarlaması 4.2 verirdi.
        foreach (var rating in new[] { 5, 4, 4, 4 })
            await CreateAsync(NewReview("doc-half", rating));
        var summary = await _client.GetFromJsonAsync<DoctorRatingSummaryDto>("/api/reviews/doctors/doc-half/summary", Ct);
        Assert.Equal(4.3, summary!.AverageRating);
    }

    [Fact]
    public async Task Summary_ForDoctorWithoutReviews_Returns200WithZeroCount()
    {
        var response = await _client.GetAsync("/api/reviews/doctors/doc-yorumsuz/summary", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = (await response.Content.ReadFromJsonAsync<DoctorRatingSummaryDto>(Ct))!;
        Assert.Equal(0, summary.ReviewCount);
        Assert.Equal(0, summary.AverageRating);
    }

    private async Task<DoctorRatingSummaryDto> SummaryAsync(string doctorId)
        => (await _client.GetFromJsonAsync<DoctorRatingSummaryDto>($"/api/reviews/doctors/{doctorId}/summary", Ct))!;

    private async Task<List<DoctorRatingSummaryDto>> SummariesAsync(string query)
        => (await _client.GetFromJsonAsync<List<DoctorRatingSummaryDto>>($"/api/reviews/summaries{query}", Ct))!;

    [Theory]
    [InlineData("doc-rec-a", new[] { 5, 4, 3 }, 67)]        // 2/3 = 66.67 -> 67
    [InlineData("doc-rec-b", new[] { 5, 5, 5, 5 }, 100)]
    [InlineData("doc-rec-c", new[] { 3, 3 }, 0)]
    [InlineData("doc-rec-d", new[] { 5, 5, 5, 3 }, 75)]     // 3/4 = 75 tam
    [InlineData("doc-rec-e", new[] { 5, 5, 5, 5, 4, 3, 2, 1 }, 63)] // 5/8 = 62.5 tam yarım: AwayFromZero 63 (banker 62 verirdi)
    public async Task Summary_RecommendationPercent_IsShareOfRatingsAtLeastFour(string doctorId, int[] ratings, int expected)
    {
        foreach (var rating in ratings)
            await CreateAsync(NewReview(doctorId, rating));
        var summary = await SummaryAsync(doctorId);
        Assert.Equal(ratings.Length, summary.ReviewCount);
        Assert.Equal(expected, summary.RecommendationPercent);
    }

    [Fact]
    public async Task Summary_RecommendationPercent_ForDoctorWithoutReviews_IsZero()
        => Assert.Equal(0, (await SummaryAsync("doc-rec-yorumsuz")).RecommendationPercent);

    [Fact]
    public async Task Summaries_ReturnsOneRecordPerRequestedId_InRequestedOrder_EvenWithoutReviews()
    {
        // Tek aggregation ($match $in + $group) kullanılır; kimlik başına ayrı sorgu (N+1) yoktur.
        foreach (var rating in new[] { 5, 4, 3 })
            await CreateAsync(NewReview("doc-bulk-b", rating));
        await CreateAsync(NewReview("doc-bulk-a", 5));
        await CreateAsync(NewReview("doc-bulk-baska", 1)); // istenmeyen doktor karışmamalı

        var list = await SummariesAsync("?doctorIds=doc-bulk-b,doc-bulk-yorumsuz,doc-bulk-a");

        Assert.Equal(["doc-bulk-b", "doc-bulk-yorumsuz", "doc-bulk-a"], list.Select(s => s.DoctorId));
        Assert.Equal((3, 4.0, 67), (list[0].ReviewCount, list[0].AverageRating, list[0].RecommendationPercent));
        Assert.Equal((0, 0.0, 0), (list[1].ReviewCount, list[1].AverageRating, list[1].RecommendationPercent));
        Assert.Equal((1, 5.0, 100), (list[2].ReviewCount, list[2].AverageRating, list[2].RecommendationPercent));
    }

    [Fact]
    public async Task Summaries_DropsDuplicatesAndBlankIds_KeepingFirstOrder()
    {
        await CreateAsync(NewReview("doc-dup-a", 5));
        var list = await SummariesAsync("?doctorIds=doc-dup-b, doc-dup-a ,,%20%20,doc-dup-b,doc-dup-a");
        Assert.Equal(["doc-dup-b", "doc-dup-a"], list.Select(s => s.DoctorId));
        Assert.Equal(1, list[1].ReviewCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?doctorIds=")]
    [InlineData("?doctorIds=,%20,")]
    public async Task Summaries_WithMissingOrEmptyIds_ReturnsEmptyList(string query)
        => Assert.Empty(await SummariesAsync(query));

    [Fact]
    public async Task Summaries_With100Ids_Returns100_But101Ids_Returns400()
    {
        string Ids(int n) => string.Join(',', Enumerable.Range(0, n).Select(i => $"doc-limit-{i}"));
        Assert.Equal(100, (await SummariesAsync($"?doctorIds={Ids(100)}")).Count);
        var response = await _client.GetAsync($"/api/reviews/summaries?doctorIds={Ids(101)}", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SummariesRoute_DoesNotCollideWithIdRoute()
    {
        // "summaries" {id} sanılsaydı GetById çalışır, 404 dönerdi; sabit rota kazanmalı: 200 ve JSON dizi.
        var response = await _client.GetAsync("/api/reviews/summaries", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync(Ct));
        // {id} yolu hâlâ çalışır.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/reviews/64b7f0f0f0f0f0f0f0f0f0f0", Ct)).StatusCode);
    }
}
