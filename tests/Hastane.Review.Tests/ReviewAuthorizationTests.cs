using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hastane.Review.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Hastane.Review.Tests;

// JWT-TASARIM.md §5/§7: Review okuması anonim, yazma yalnız Patient, PUT/DELETE Admin ya da yorumun sahibi.
public class ReviewAuthorizationTests : IClassFixture<ReviewApiFactory>
{
    private const string UnknownId = "64b7f0f0f0f0f0f0f0f0f0f0";

    private readonly ReviewApiFactory _factory;

    public ReviewAuthorizationTests(ReviewApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientWith(string? token)
    {
        var client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    private static string AdminToken => TestJwt.Create("admin-1", [HastaneRoles.Admin]);

    private static string PatientToken(string userId, string? name = null, string? email = null)
        => TestJwt.Create(userId, [HastaneRoles.Patient], name: name, email: email);

    private static string DoctorToken => TestJwt.Create("doctor-user", [HastaneRoles.Doctor], "d1");

    private static string BadToken(string kind)
    {
        var roles = new[] { HastaneRoles.Admin, HastaneRoles.Patient }; // rol iddiası sahte token'ı kurtarmaz
        return kind switch
        {
            "expired" => TestJwt.CreateExpired("admin-1", roles),
            "broken-signature" => TestJwt.CreateWithBrokenSignature("admin-1", roles),
            "alg-none" => TestJwt.CreateAlgNone("admin-1", roles),
            _ => TestJwt.CreateHs256WithPublicKeyAsSecret("admin-1", roles),
        };
    }

    private async Task<ResultReviewDto> CreateAsPatientAsync(string userId, string doctorId = "doc-authz", string name = "Hasta Bir")
    {
        var response = await ClientWith(PatientToken(userId, name)).PostAsJsonAsync("/api/reviews",
            new CreateReviewDto { DoctorId = doctorId, Rating = 4, Comment = "İyi" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResultReviewDto>(Ct))!;
    }

    // Mongo'daki ham belge: ReviewerId API çıktısında yok, doğrulamak için veritabanına doğrudan bakılır.
    private async Task<BsonDocument> RawAsync(string id)
    {
        var client = new MongoClient(_factory.MongoConnectionString);
        var raw = client.GetDatabase("hastane_review_test").GetCollection<BsonDocument>("reviews");
        return await raw.Find(new BsonDocument("_id", ObjectId.Parse(id))).SingleAsync(Ct);
    }

    private async Task<string> InsertLegacyReviewAsync()
    {
        // ReviewerId alanı olmayan eski belge (kimlik doğrulama öncesi yazılmış).
        var client = new MongoClient(_factory.MongoConnectionString);
        var raw = client.GetDatabase("hastane_review_test").GetCollection<BsonDocument>("reviews");
        var id = ObjectId.GenerateNewId();
        await raw.InsertOneAsync(new BsonDocument
        {
            { "_id", id }, { "DoctorId", "doc-legacy" }, { "ReviewerName", "Eski Hasta" },
            { "Rating", 3 }, { "Comment", "Eski" }, { "CreatedAt", DateTime.UtcNow },
        }, cancellationToken: Ct);
        return id.ToString();
    }

    // ---- kapsam ----

    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(_factory.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    // ---- anonim okuma ----

    [Fact]
    public async Task AnonymousReads_Return200WithoutToken()
    {
        var created = await CreateAsPatientAsync("patient-anon");
        var anonymous = ClientWith(null);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/reviews", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/reviews?doctorId=doc-authz", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/reviews/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/reviews/doctors/doc-authz/summary", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/reviews/summaries?doctorIds=doc-authz", Ct)).StatusCode);
    }

    [Fact]
    public async Task ResultDto_DoesNotLeakReviewerId()
    {
        var created = await CreateAsPatientAsync("patient-gizli-kimlik");

        var json = await ClientWith(null).GetStringAsync($"/api/reviews/{created.Id}", Ct);
        var list = await ClientWith(null).GetStringAsync("/api/reviews", Ct);

        Assert.DoesNotContain("reviewerId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("patient-gizli-kimlik", json);
        Assert.DoesNotContain("patient-gizli-kimlik", list);
        Assert.Contains("reviewerName", json);
    }

    // ---- 401 ----

    [Fact]
    public async Task Writes_WithoutToken_Return401()
    {
        var anonymous = ClientWith(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/reviews",
            new CreateReviewDto { DoctorId = "d", Rating = 3 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/reviews/{UnknownId}",
            new UpdateReviewDto { Id = UnknownId, Rating = 3 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/reviews/{UnknownId}", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("broken-signature")]
    [InlineData("alg-none")]
    [InlineData("hs256-public-key-as-secret")]
    public async Task Writes_WithInvalidToken_Return401(string kind)
    {
        var client = ClientWith(BadToken(kind));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/reviews",
            new CreateReviewDto { DoctorId = "d", Rating = 3 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync($"/api/reviews/{UnknownId}",
            new UpdateReviewDto { Id = UnknownId, Rating = 3 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync($"/api/reviews/{UnknownId}", Ct)).StatusCode);
    }

    // ---- 403 (yanlış rol) ----

    [Fact]
    public async Task Post_ByAdmin_Returns403_OnlyPatientMayWrite()
    {
        var response = await ClientWith(AdminToken).PostAsJsonAsync("/api/reviews",
            new CreateReviewDto { DoctorId = "d", Rating = 3 }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_ByDoctor_Returns403()
    {
        var response = await ClientWith(DoctorToken).PostAsJsonAsync("/api/reviews",
            new CreateReviewDto { DoctorId = "d", Rating = 3 }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutAndDelete_ByDoctorRole_Return403_AndReviewUnchanged()
    {
        var created = await CreateAsPatientAsync("patient-doktor-deneme");
        var doctor = ClientWith(DoctorToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.PutAsJsonAsync($"/api/reviews/{created.Id}",
            new UpdateReviewDto { Id = created.Id, Rating = 1 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.DeleteAsync($"/api/reviews/{created.Id}", Ct)).StatusCode);
        Assert.Equal(4, (await RawAsync(created.Id))["Rating"].AsInt32);
    }

    // ---- POST: sahip token'dan, gövdeden değil ----

    [Fact]
    public async Task Post_ByPatient_SetsReviewerFromToken()
    {
        var token = PatientToken("patient-77", name: "Ayşe Yılmaz");

        var response = await ClientWith(token).PostAsJsonAsync("/api/reviews",
            new CreateReviewDto { DoctorId = "doc-token", Rating = 5, Comment = "Süper" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResultReviewDto>(Ct))!;
        Assert.Equal("Ayşe Yılmaz", created.ReviewerName);
        Assert.Equal("patient-77", (await RawAsync(created.Id))["ReviewerId"].AsString);
    }

    [Fact]
    public async Task Post_WithoutNameClaim_FallsBackToEmail()
    {
        // TestJwt her zaman name yazar; name claim'i olmayan token elle üretilir.
        var token = TestJwt.Create("patient-adsiz", [HastaneRoles.Patient], email: "adsiz@hasta.test", name: "");

        var response = await ClientWith(token).PostAsJsonAsync("/api/reviews",
            new CreateReviewDto { DoctorId = "doc-token", Rating = 5 }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResultReviewDto>(Ct))!;
        Assert.Equal("adsiz@hasta.test", created.ReviewerName);
    }

    [Fact]
    public async Task Post_BodyWithReviewerIdAndName_DoesNotOverrideToken()
    {
        var token = PatientToken("patient-gercek", name: "Gerçek Hasta");
        var body = """{"doctorId":"doc-mass","rating":5,"reviewerId":"patient-kurban","reviewerName":"Sahte Ad"}""";

        var response = await ClientWith(token).PostAsync("/api/reviews", new StringContent(body, Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResultReviewDto>(Ct))!;
        Assert.Equal("Gerçek Hasta", created.ReviewerName);
        var raw = await RawAsync(created.Id);
        Assert.Equal("patient-gercek", raw["ReviewerId"].AsString);
        Assert.Equal("Gerçek Hasta", raw["ReviewerName"].AsString);
    }

    // ---- PUT / DELETE: Admin ya da sahip ----

    [Fact]
    public async Task Put_ByOwner_Returns204_AndKeepsOwnerFields()
    {
        var created = await CreateAsPatientAsync("patient-sahip", name: "Sahip Hasta");
        var body = """{"id":"__ID__","rating":2,"comment":"Değişti","reviewerId":"patient-baska","reviewerName":"Sahte"}""".Replace("__ID__", created.Id);

        var put = await ClientWith(PatientToken("patient-sahip", "Sahip Hasta")).PutAsync($"/api/reviews/{created.Id}",
            new StringContent(body, Encoding.UTF8, "application/json"), Ct);

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var raw = await RawAsync(created.Id);
        Assert.Equal(2, raw["Rating"].AsInt32);
        Assert.Equal("Değişti", raw["Comment"].AsString);
        Assert.Equal("patient-sahip", raw["ReviewerId"].AsString);
        Assert.Equal("Sahip Hasta", raw["ReviewerName"].AsString);
    }

    [Fact]
    public async Task Put_ByOtherPatient_Returns404_AndReviewUnchanged()
    {
        var created = await CreateAsPatientAsync("patient-a");

        var put = await ClientWith(PatientToken("patient-b")).PutAsJsonAsync($"/api/reviews/{created.Id}",
            new UpdateReviewDto { Id = created.Id, Rating = 1, Comment = "Ele geçirildi" }, Ct);

        // 403 değil 404: başkasının yorumunun var olduğunu da öğretmeyiz ("yok" ile aynı cevap).
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        var raw = await RawAsync(created.Id);
        Assert.Equal(4, raw["Rating"].AsInt32);
        Assert.Equal("İyi", raw["Comment"].AsString);
    }

    [Fact]
    public async Task Put_ByAdmin_Returns204_ForAnyReview()
    {
        var created = await CreateAsPatientAsync("patient-admin-put");

        var put = await ClientWith(AdminToken).PutAsJsonAsync($"/api/reviews/{created.Id}",
            new UpdateReviewDto { Id = created.Id, Rating = 1, Comment = "Moderasyon" }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var raw = await RawAsync(created.Id);
        Assert.Equal(1, raw["Rating"].AsInt32);
        Assert.Equal("patient-admin-put", raw["ReviewerId"].AsString); // Admin sahipliği devralmaz
    }

    [Fact]
    public async Task Delete_ByOwner_Returns204()
    {
        var created = await CreateAsPatientAsync("patient-siler");

        var del = await ClientWith(PatientToken("patient-siler")).DeleteAsync($"/api/reviews/{created.Id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ClientWith(null).GetAsync($"/api/reviews/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Delete_ByOtherPatient_Returns404_AndReviewStays()
    {
        var created = await CreateAsPatientAsync("patient-a");

        var del = await ClientWith(PatientToken("patient-b")).DeleteAsync($"/api/reviews/{created.Id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientWith(null).GetAsync($"/api/reviews/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Delete_ByAdmin_Returns204_ForAnyReview()
    {
        var created = await CreateAsPatientAsync("patient-admin-del");

        Assert.Equal(HttpStatusCode.NoContent, (await ClientWith(AdminToken).DeleteAsync($"/api/reviews/{created.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task OwnerRule_SameAnswerAsMissing_ForUnknownId()
    {
        // Sahip olmayan için 404 ile hiç olmayan kayıt için 404 ayırt edilemez.
        var patient = ClientWith(PatientToken("patient-x"));
        Assert.Equal(HttpStatusCode.NotFound, (await patient.DeleteAsync($"/api/reviews/{UnknownId}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await patient.PutAsJsonAsync($"/api/reviews/{UnknownId}",
            new UpdateReviewDto { Id = UnknownId, Rating = 3 }, Ct)).StatusCode);
    }

    // ---- ReviewerId'si olmayan eski belge: yalnız Admin ----

    [Fact]
    public async Task LegacyReviewWithoutReviewerId_Patient404_AdminCanModifyAndDelete()
    {
        var id = await InsertLegacyReviewAsync();
        var patient = ClientWith(PatientToken("patient-eskiyi-sahiplenmeye-calisan", name: "Eski Hasta"));

        // Adı eşleşse bile sahibi bilinmeyen belgeyi bir Patient değiştiremez/silemez.
        Assert.Equal(HttpStatusCode.NotFound, (await patient.PutAsJsonAsync($"/api/reviews/{id}",
            new UpdateReviewDto { Id = id, Rating = 1 }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await patient.DeleteAsync($"/api/reviews/{id}", Ct)).StatusCode);
        Assert.Equal(3, (await RawAsync(id))["Rating"].AsInt32);

        var admin = ClientWith(AdminToken);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/reviews/{id}",
            new UpdateReviewDto { Id = id, Rating = 5 }, Ct)).StatusCode);
        Assert.Equal(5, (await RawAsync(id))["Rating"].AsInt32);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/reviews/{id}", Ct)).StatusCode);
    }
}
