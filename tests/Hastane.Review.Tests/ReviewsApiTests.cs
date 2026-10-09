using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hastane.Review.Api.Dtos;

namespace Hastane.Review.Tests;

public class ReviewsApiTests : IClassFixture<ReviewApiFactory>
{
    private readonly HttpClient _client;

    public ReviewsApiTests(ReviewApiFactory factory) => _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateReviewDto NewReview(string doctorId, int rating = 5, string reviewer = "Ali") => new()
    {
        DoctorId = doctorId,
        ReviewerName = reviewer,
        Rating = rating,
        Comment = "Yorum",
    };

    private async Task<ResultReviewDto> CreateAsync(CreateReviewDto dto)
    {
        var response = await _client.PostAsJsonAsync("/api/reviews", dto, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<ResultReviewDto>(Ct))!;
    }

    [Fact]
    public async Task Create_ThenList_ContainsReview()
    {
        var created = await CreateAsync(NewReview("doc-list", 4, "Veli"));
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
        var body = """{"doctorId":"doc-time","reviewerName":"Ali","rating":5,"createdAt":"2000-01-01T00:00:00Z"}""";
        var response = await _client.PostAsync("/api/reviews",
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
        var first = await CreateAsync(NewReview("doc-sort-x", reviewer: "Birinci"));
        await Task.Delay(20, Ct);
        var second = await CreateAsync(NewReview("doc-sort-x", reviewer: "İkinci"));
        await CreateAsync(NewReview("doc-sort-y"));

        var list = await _client.GetFromJsonAsync<List<ResultReviewDto>>("/api/reviews?doctorId=doc-sort-x", Ct);
        Assert.Equal([second.Id, first.Id], list!.Select(r => r.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Create_WithRatingOutOfRange_Returns400(int rating)
    {
        var response = await _client.PostAsJsonAsync("/api/reviews", NewReview("doc-bad", rating), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithTooLongComment_Returns400()
    {
        var dto = NewReview("doc-bad");
        dto.Comment = new string('a', 1001);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/reviews", dto, Ct)).StatusCode);
    }

    [Fact]
    public async Task Create_WithoutReviewerName_Returns400()
    {
        var dto = NewReview("doc-bad");
        dto.ReviewerName = "";
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/reviews", dto, Ct)).StatusCode);
    }

    [Fact]
    public async Task Update_ChangesFields_ButKeepsDoctorIdAndCreatedAt()
    {
        var created = await CreateAsync(NewReview("doc-upd", 2, "Eski"));
        // DTO'da olmayan doctorId ve createdAt fazladan gönderilir; yok sayılmalı.
        var update = new
        {
            id = created.Id,
            doctorId = "baska-doktor",
            createdAt = "2000-01-01T00:00:00Z",
            reviewerName = "Yeni",
            rating = 5,
            comment = "Güncel",
        };
        var put = await _client.PutAsJsonAsync($"/api/reviews/{created.Id}", update, Ct);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var dto = await _client.GetFromJsonAsync<ResultReviewDto>($"/api/reviews/{created.Id}", Ct);
        Assert.Equal("Yeni", dto!.ReviewerName);
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
            new UpdateReviewDto { Id = id, ReviewerName = "x", Rating = 3 }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Put_WithMismatchedId_Returns400()
    {
        var created = await CreateAsync(NewReview("doc-mismatch"));
        var put = await _client.PutAsJsonAsync($"/api/reviews/{created.Id}",
            new UpdateReviewDto { Id = "64b7f0f0f0f0f0f0f0f0f0f0", ReviewerName = "x", Rating = 3 }, Ct);
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
}
