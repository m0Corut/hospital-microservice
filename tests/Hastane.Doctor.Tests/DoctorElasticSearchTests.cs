using System.Net;
using System.Net.Http.Json;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using MongoDB.Bson;
using MongoDB.Driver;
using static Hastane.Doctor.Tests.TestSupport;

namespace Hastane.Doctor.Tests;

// GERÇEK Elasticsearch 8.15 + MongoDB (Testcontainers). Her test kendi benzersiz etiketini (tag) BranchId ve/veya hizmet
// olarak kullanır: aynı konteynerdeki başka testlerin verisi sonuçları bozmaz.
[Collection("elastic")]
public class DoctorElasticSearchTests
{
    private readonly ElasticDoctorApiFactory _factory;
    private readonly HttpClient _admin;      // Elasticsearch AÇIK
    private readonly HttpClient _mongoOnly;  // aynı Mongo, Elasticsearch KAPALI

    public DoctorElasticSearchTests(ElasticDoctorApiFactory factory)
    {
        _factory = factory;
        var adminToken = TestJwt.Create("admin-1", [HastaneRoles.Admin]);
        _admin = factory.CreateClient().WithBearer(adminToken);
        _mongoOnly = factory.WithoutElastic().CreateClient().WithBearer(adminToken);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(PagedResult<DoctorSearchItemDto> Page, string Engine)> SearchEngine(HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/doctors/search" + query, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var engine = response.Headers.GetValues("X-Search-Engine").Single();
        var page = (await response.Content.ReadFromJsonAsync<PagedResult<DoctorSearchItemDto>>(Json, Ct))!;
        return (page, engine);
    }

    private static CreateDoctorDto Doc(string first, string last, string tag, DoctorGender gender = DoctorGender.Female,
        string[]? services = null, string? degrees = null, string? biography = null, ClinicLocationDto[]? locations = null)
    {
        var dto = NewDoc(first, last, tag, gender, locations ?? []);
        dto.Services = [.. services ?? []];
        dto.Degrees = degrees;
        dto.Biography = biography;
        return dto;
    }

    private static UpdateDoctorDto ToUpdate(string id, CreateDoctorDto d) => new()
    {
        Id = id,
        FirstName = d.FirstName,
        LastName = d.LastName,
        Title = d.Title,
        Gender = d.Gender,
        BranchId = d.BranchId,
        Degrees = d.Degrees,
        Biography = d.Biography,
        Services = d.Services,
        ClinicLocations = d.ClinicLocations,
    };

    private static List<string> Ids(PagedResult<DoctorSearchItemDto> p) => [.. p.Items.Select(i => i.Id)];

    private static string NextDate(DayOfWeek day)
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(1);
        while (d.DayOfWeek != day) d = d.AddDays(1);
        return d.ToString("yyyy-MM-dd");
    }

    // ---- Türkçe / ASCII, önek, yazım hatası, alaka ----

    [Fact]
    public async Task AsciiQuery_FindsTurkishName_WhereMongoRegexCannot()
    {
        var tag = NewTag();
        var d = await _admin.CreateOk(Doc("Mehmet", "Yılmaz", tag), Ct);

        foreach (var text in new[] { "Yilmaz", "YILMAZ", "yılmaz", "Yılmaz" })
        {
            var (es, engine) = await SearchEngine(_admin, $"?branchIds={tag}&text={Uri.EscapeDataString(text)}");
            Assert.Equal("elasticsearch", engine);
            Assert.Equal([d.Id], Ids(es));
        }

        // Aynı soru Mongo regex yolunda: "Yilmaz" ≠ "Yılmaz" (ı harfi), kayıt bulunamaz. Dizinin varlık nedeni bu.
        var (mongo, mongoEngine) = await SearchEngine(_mongoOnly, $"?branchIds={tag}&text=Yilmaz");
        Assert.Equal("mongo", mongoEngine);
        Assert.Empty(mongo.Items);
    }

    [Fact]
    public async Task AsciiQuery_FindsTurkishService_Cocuk()
    {
        var tag = NewTag();
        var d = await _admin.CreateOk(Doc("Ayşe", "Test", tag, services: ["Çocuk Sağlığı"]), Ct);

        var (es, engine) = await SearchEngine(_admin, $"?branchIds={tag}&text=cocuk");
        Assert.Equal("elasticsearch", engine);
        Assert.Equal([d.Id], Ids(es));
        Assert.Equal([d.Id], Ids((await SearchEngine(_admin, $"?branchIds={tag}&text=SAGLIGI")).Page));
    }

    [Fact]
    public async Task Typo_IsTolerated_ByFuzziness()
    {
        var tag = NewTag();
        var d = await _admin.CreateOk(Doc("Ayşe", "Test", tag, services: ["Kardiyoloji"]), Ct);

        var (es, engine) = await SearchEngine(_admin, $"?branchIds={tag}&text=kardiyolji");
        Assert.Equal("elasticsearch", engine);
        Assert.Equal([d.Id], Ids(es));

        // Mongo regex yazım hatasını bilmez.
        Assert.Empty((await SearchEngine(_mongoOnly, $"?branchIds={tag}&text=kardiyolji")).Page.Items);
    }

    [Fact]
    public async Task Prefix_MatchesAsYouType()
    {
        var tag = NewTag();
        var d = await _admin.CreateOk(Doc("Mehmet", "Yılmaz", tag), Ct);

        Assert.Equal([d.Id], Ids((await SearchEngine(_admin, $"?branchIds={tag}&text=yilm")).Page));
        Assert.Equal([d.Id], Ids((await SearchEngine(_admin, $"?branchIds={tag}&text=" + Uri.EscapeDataString("meh yil"))).Page));
    }

    [Fact]
    public async Task DefaultSort_WithText_IsRelevance_NameMatchBeatsBiographyMention()
    {
        var tag = NewTag();
        // Soyadı alfabetik olarak SONRA gelen ama adı eşleşen doktor, yalnız biyografisinde geçenden önce gelmeli.
        var byBio = await _admin.CreateOk(Doc("Zeynep", "Aksoy", tag, biography: "Uzun yıllar Ayşe hanımla birlikte çalıştı."), Ct);
        var byName = await _admin.CreateOk(Doc("Ayşe", "Yıldız", tag), Ct);

        var (rel, engine) = await SearchEngine(_admin, $"?branchIds={tag}&text=ayse");
        Assert.Equal("elasticsearch", engine);
        Assert.Equal([byName.Id, byBio.Id], Ids(rel));

        // Açık name sıralaması mevcut kuralı korur: soyada göre.
        var named = await SearchEngine(_admin, $"?branchIds={tag}&text=ayse&sort=name");
        Assert.Equal([byBio.Id, byName.Id], Ids(named.Page));
    }

    [Fact]
    public async Task WithoutText_UsesMongo_EvenWhenElasticsearchIsOn()
    {
        var tag = NewTag();
        await _admin.CreateOk(Doc("A", "B", tag), Ct);
        var (page, engine) = await SearchEngine(_admin, $"?branchIds={tag}");
        Assert.Equal("mongo", engine);
        Assert.Single(page.Items);
    }

    // ---- İki yol aynı sonucu vermeli (ES ve Mongo karşılaştırması) ----

    [Fact]
    public async Task Filters_GiveSameResult_OnElasticsearchAndMongoPaths()
    {
        var tag = NewTag();
        var tuesday = NextDate(DayOfWeek.Tuesday);
        string[] svc = [tag];
        await _admin.CreateOk(Doc("Ali", "Aksoy", tag, DoctorGender.Male, svc, locations:
            [Loc("Ankara", 200m, "K1", "TRY", Slot(DayOfWeek.Monday), Slot(DayOfWeek.Tuesday))]), Ct);
        await _admin.CreateOk(Doc("Ayşe", "Bulut", tag, DoctorGender.Female, svc, locations:
            [Loc("Ankara", 500m, "K2", "TRY", Slot(DayOfWeek.Tuesday)), Loc("İzmir", 100m, "K3", "TRY", Slot(DayOfWeek.Friday))]), Ct);
        await _admin.CreateOk(Doc("Can", "Çelik", tag, DoctorGender.Male, svc, locations:
            [Loc("İzmir", 900m, "K4", "TRY", Slot(DayOfWeek.Monday))]), Ct);
        await _admin.CreateOk(Doc("Deniz", "Demir", tag, DoctorGender.Female, svc, locations:
            [Loc("Ankara", null, "K5", "TRY", Slot(DayOfWeek.Tuesday))]), Ct);
        await _admin.CreateOk(Doc("Ece", "Eren", tag, DoctorGender.Female, svc), Ct);

        string[] variants =
        [
            "",
            "&gender=Female",
            "&gender=male",
            "&city=Ankara",
            "&city=" + "%C4%B0zmir",
            "&minFee=200&maxFee=600",
            "&minFee=100",
            "&maxFee=500&city=Ankara",     // aynı konumda şehir + ücret
            "&city=Ankara&minFee=700",     // Ankara'da pahalı yok (İzmir'deki 900 sayılmaz)
            $"&date={tuesday}",
            $"&date={tuesday}&city=Ankara&minFee=300",
            "&sort=feeAsc",
            "&sort=feeDesc",
            "&sort=feeAsc&pageSize=2&page=2",
            "&sort=name&pageSize=2&page=3",
            "&gender=Male&sort=feeDesc",
        ];

        foreach (var v in variants)
        {
            var q = $"?branchIds={tag}&text={tag}{v}" + (v.Contains("sort=") ? "" : "&sort=name");
            var (es, esEngine) = await SearchEngine(_admin, q);
            var (mongo, mongoEngine) = await SearchEngine(_mongoOnly, q);
            Assert.Equal("elasticsearch", esEngine);
            Assert.Equal("mongo", mongoEngine);
            Assert.True(Ids(mongo).SequenceEqual(Ids(es)), $"Sıra/kimlik farkı: {v}");
            Assert.Equal(mongo.TotalCount, es.TotalCount);
        }

        // Boş olmadığından emin ol (iki yol da boşsa karşılaştırma anlamsız olurdu).
        Assert.Equal(5, (await SearchEngine(_admin, $"?branchIds={tag}&text={tag}&sort=name")).Page.TotalCount);
    }

    // ---- Yazma yolu: oluştur / güncelle / sil dizine yansır ----

    [Fact]
    public async Task CreateUpdateDelete_AreReflectedInTheIndex()
    {
        var tag = NewTag();
        var dto = Doc("Mehmet", "Yılmaz", tag, services: [tag]);
        var created = await _admin.CreateOk(dto, Ct);
        Assert.Equal([created.Id], Ids((await SearchEngine(_admin, $"?branchIds={tag}&text={tag}")).Page));

        // Güncelle: hizmet etiketi değişir -> eskisiyle bulunmaz, yenisiyle bulunur.
        var tag2 = NewTag();
        dto.Services = [tag2];
        var put = await _admin.PutAsJsonAsync($"/api/doctors/{created.Id}", ToUpdate(created.Id, dto), Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Empty((await SearchEngine(_admin, $"?branchIds={tag}&text={tag}")).Page.Items);
        Assert.Equal([created.Id], Ids((await SearchEngine(_admin, $"?branchIds={tag}&text={tag2}")).Page));

        // Sil: dizinden de gider.
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/doctors/{created.Id}", Ct)).StatusCode);
        var after = await SearchEngine(_admin, $"?branchIds={tag}&text={tag2}");
        Assert.Empty(after.Page.Items);
        Assert.Equal(0, after.Page.TotalCount);
    }

    // ---- Dizin KAPALI/ulaşılamaz: Mongo'ya düş, yazma yine başarılı; sonra rebuild ile onar ----

    [Fact]
    public async Task ElasticDown_SearchFallsBackToMongo_WritesStillSucceed_AndRebuildRepairsTheDrift()
    {
        var tag = NewTag();
        var broken = _factory.WithBrokenElastic().CreateClient().WithBearer(TestJwt.Create("admin-1", [HastaneRoles.Admin]));

        // Dizine ulaşılamıyor ama oluşturma 201 (kaynak gerçek Mongo yazıldı, dizin best-effort).
        var created = await broken.CreateOk(Doc("Mehmet", "Yılmaz", tag, services: [tag]), Ct);

        // Arama: dizin yok -> Mongo yolu, başlık "mongo", kayıt bulunur (düz alt dize eşleşir).
        var (viaMongo, engine) = await SearchEngine(broken, $"?branchIds={tag}&text={tag}");
        Assert.Equal("mongo", engine);
        Assert.Equal([created.Id], Ids(viaMongo));

        // Sağlıklı dizin bu kaydı HİÇ görmedi: türetilmiş kopya geride kaldı (tutarsızlık).
        var (drift, esEngine) = await SearchEngine(_admin, $"?branchIds={tag}&text={tag}");
        Assert.Equal("elasticsearch", esEngine);
        Assert.Empty(drift.Items);

        // Rebuild: Mongo'dan yeniden kurar.
        var rebuild = await _admin.PostAsync("/api/doctors/search-index/rebuild", null, Ct);
        Assert.Equal(HttpStatusCode.OK, rebuild.StatusCode);
        var result = (await rebuild.Content.ReadFromJsonAsync<SearchIndexRebuildResultDto>(Json, Ct))!;
        Assert.True(result.Indexed >= 1);
        Assert.Equal([created.Id], Ids((await SearchEngine(_admin, $"?branchIds={tag}&text={tag}")).Page));
    }

    [Fact]
    public async Task Rebuild_RemovesOrphans_AndStatusReportsInSync()
    {
        var tag = NewTag();
        var created = await _admin.CreateOk(Doc("Mehmet", "Yılmaz", tag, services: [tag]), Ct);

        // API'yi atlayıp Mongo'dan sil: dizinde YETİM kayıt kalır.
        await _factory.RawCollection.DeleteOneAsync(new BsonDocument("_id", new ObjectId(created.Id)), Ct);
        var orphan = await SearchEngine(_admin, $"?branchIds={tag}&text={tag}");
        Assert.Empty(orphan.Page.Items);          // belge Mongo'dan getirilemedi, sessizce atlandı
        Assert.Equal(1, orphan.Page.TotalCount);  // ama dizin hâlâ 1 diyor: tutarsızlık görünür
        var drifted = await GetStatus();
        Assert.False(drifted.InSync);

        var rebuild = await _admin.PostAsync("/api/doctors/search-index/rebuild", null, Ct);
        Assert.Equal(HttpStatusCode.OK, rebuild.StatusCode);

        Assert.Equal(0, (await SearchEngine(_admin, $"?branchIds={tag}&text={tag}")).Page.TotalCount);
        var status = await GetStatus();
        Assert.True(status.Enabled);
        Assert.True(status.Reachable);
        Assert.Equal(ElasticDoctorApiFactory.IndexName, status.Index);
        Assert.True(status.InSync);
        Assert.Equal(status.MongoCount, status.IndexedCount);
    }

    private async Task<SearchIndexStatusDto> GetStatus()
    {
        var response = await _admin.GetAsync("/api/doctors/search-index/status", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SearchIndexStatusDto>(Json, Ct))!;
    }

    // ---- Öneri ----

    [Fact]
    public async Task Suggest_FindsDoctorsAndServices_IgnoringTurkishCharacters()
    {
        var tag = NewTag();
        var d = await _admin.CreateOk(Doc("Mehmet", "Yılmaz" + tag, tag, services: ["Çocuk Sağlığı " + tag]), Ct);

        var byName = await Suggest(_admin, "yilmaz" + tag);
        Assert.Contains(byName, s => s.Type == "doctor" && s.DoctorId == d.Id && s.Text == "Mehmet Yılmaz" + tag);

        var byService = await Suggest(_admin, "cocuk sag");
        Assert.Contains(byService, s => s.Type == "service" && s.Text == "Çocuk Sağlığı " + tag);
    }

    [Fact]
    public async Task Suggest_ReturnsAtMostEight()
    {
        var tag = NewTag();
        for (var i = 0; i < 10; i++)
            await _admin.CreateOk(Doc(tag + "ad", "Soyad" + i, tag), Ct);

        var list = await Suggest(_admin, tag + "ad");
        Assert.Equal(8, list.Count);
        Assert.All(list, s => Assert.StartsWith(tag + "ad", s.Text));
    }

    private static async Task<List<DoctorSuggestionDto>> Suggest(HttpClient client, string q)
    {
        var response = await client.GetAsync("/api/doctors/suggest?q=" + Uri.EscapeDataString(q), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<DoctorSuggestionDto>>(Json, Ct))!;
    }
}
