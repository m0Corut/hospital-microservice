using System.Net;
using System.Net.Http.Json;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Search;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using static Hastane.Doctor.Tests.TestSupport;

namespace Hastane.Doctor.Tests;

// Elasticsearch KAPALI (varsayılan) iken davranış + yönetim uçlarının yetkisi. Docker'da yalnız MongoDB gerekir.
public class DoctorSearchIndexDisabledTests : IClassFixture<DoctorApiFactory>
{
    private readonly DoctorApiFactory _factory;

    public DoctorSearchIndexDisabledTests(DoctorApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientWith(string? token)
    {
        var client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    private static string AdminToken => TestJwt.Create("admin-1", [HastaneRoles.Admin]);
    private static string DoctorToken => TestJwt.Create("doctor-user", [HastaneRoles.Doctor], "64b7f0f0f0f0f0f0f0f0f0f0");
    private static string PatientToken => TestJwt.Create("patient-1", [HastaneRoles.Patient]);

    // ---- Arama başlığı ----

    [Fact]
    public async Task Search_WhenElasticsearchIsOff_AnswersFromMongo_WithHeader()
    {
        var admin = ClientWith(AdminToken);
        var tag = NewTag();
        await admin.CreateOk(NewDoc("Ayşe", "Test", tag), Ct);

        foreach (var q in new[] { $"?branchIds={tag}", $"?branchIds={tag}&text=ayşe" })
        {
            var response = await admin.GetAsync("/api/doctors/search" + q, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("mongo", response.Headers.GetValues("X-Search-Engine").Single());
            var page = (await response.Content.ReadFromJsonAsync<PagedResult<DoctorSearchItemDto>>(Json, Ct))!;
            Assert.Single(page.Items);
        }
    }

    [Fact]
    public async Task Search_InvalidQuery_StillReturns400_BeforeAnyEngineChoice()
    {
        var response = await ClientWith(null).GetAsync("/api/doctors/search?text=ali&gender=Alien", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Öneri: Mongo regex yedeği ----

    [Fact]
    public async Task Suggest_WithoutElasticsearch_UsesSimpleMongoSuggestions_AtMostEight()
    {
        var admin = ClientWith(AdminToken);
        var tag = NewTag();
        var one = NewDoc("Mehmet", "Yılmaz" + tag, tag);
        one.Services = ["Çocuk Sağlığı " + tag];
        await admin.CreateOk(one, Ct);
        for (var i = 0; i < 10; i++) await admin.CreateOk(NewDoc(tag + "ad", "Soyad" + i, tag), Ct);

        var anon = ClientWith(null);
        var byName = await anon.GetFromJsonAsync<List<DoctorSuggestionDto>>("/api/doctors/suggest?q=" + Uri.EscapeDataString("Mehmet Yılmaz" + tag), Json, Ct);
        Assert.Contains(byName!, s => s.Type == "doctor" && s.Text == "Mehmet Yılmaz" + tag);

        var byService = await anon.GetFromJsonAsync<List<DoctorSuggestionDto>>("/api/doctors/suggest?q=" + Uri.EscapeDataString("Çocuk Sağlığı " + tag), Json, Ct);
        Assert.Contains(byService!, s => s.Type == "service");

        var many = await anon.GetFromJsonAsync<List<DoctorSuggestionDto>>("/api/doctors/suggest?q=" + tag + "ad", Json, Ct);
        Assert.Equal(8, many!.Count);
    }

    [Fact]
    public async Task Suggest_EmptyQuery_ReturnsEmptyList_AndTooLongQuery_Returns400()
    {
        var anon = ClientWith(null);
        Assert.Empty((await anon.GetFromJsonAsync<List<DoctorSuggestionDto>>("/api/doctors/suggest", Json, Ct))!);
        Assert.Empty((await anon.GetFromJsonAsync<List<DoctorSuggestionDto>>("/api/doctors/suggest?q=%20%20", Json, Ct))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.GetAsync("/api/doctors/suggest?q=" + new string('a', 101), Ct)).StatusCode);
    }

    // ---- Yönetim uçları: yalnız Admin ----

    public static TheoryData<string, string> AdminOnlyEndpoints => new()
    {
        { "GET", "/api/doctors/search-index/status" },
        { "POST", "/api/doctors/search-index/rebuild" },
    };

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task SearchIndexAdmin_Anonymous_Returns401(string method, string path)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await Send(null, method, path)).StatusCode);

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task SearchIndexAdmin_PatientAndDoctor_Return403(string method, string path)
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(PatientToken, method, path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(DoctorToken, method, path)).StatusCode);
    }

    [Fact]
    public async Task Status_AsAdmin_ReportsDisabledIndex_WithMongoCount()
    {
        var admin = ClientWith(AdminToken);
        await admin.CreateOk(NewDoc("A", "B", NewTag()), Ct);

        var status = await admin.GetFromJsonAsync<SearchIndexStatusDto>("/api/doctors/search-index/status", Json, Ct);
        Assert.False(status!.Enabled);
        Assert.False(status.Reachable);
        Assert.Null(status.IndexedCount);
        Assert.True(status.MongoCount >= 1);
    }

    [Fact]
    public async Task Rebuild_AsAdmin_WhenIndexIsOff_Returns409()
    {
        var response = await Send(AdminToken, "POST", "/api/doctors/search-index/rebuild");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private Task<HttpResponseMessage> Send(string? token, string method, string path)
        => ClientWith(token).SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);
}

// Saf birim testleri (Docker yok): C# tarafındaki Türkçe katlama, dizinin analizörüyle aynı fikri uygular.
public class TextFoldTests
{
    [Theory]
    [InlineData("Yılmaz", "yilmaz")]
    [InlineData("YILMAZ", "yilmaz")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("Çocuk Sağlığı", "cocuk sagligi")]
    [InlineData("Öğüş", "ogus")]
    [InlineData("ŞEKER", "seker")]
    public void Fold_MapsTurkishLettersToAscii(string input, string expected)
        => Assert.Equal(expected, TextFold.Fold(input));

    [Theory]
    [InlineData("Mehmet Yılmaz", "yilm", true)]
    [InlineData("Mehmet Yılmaz", "meh yil", true)]
    [InlineData("Mehmet Yılmaz", "yilmaz mehmet", true)]
    [InlineData("Mehmet Yılmaz", "kaya", false)]
    [InlineData("Mehmet Yılmaz", "", false)]
    public void MatchesPrefixes_RequiresEveryQueryWordToBeAPrefix(string candidate, string query, bool expected)
        => Assert.Equal(expected, TextFold.MatchesPrefixes(candidate, query));
}
