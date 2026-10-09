using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Entities;
using static Hastane.Doctor.Tests.TestSupport;

namespace Hastane.Doctor.Tests;

// Her test kendi benzersiz etiketini (tag) BranchId olarak kullanır ve aramayı ona kilitler:
// aynı konteynerde paralel/önceki testlerin verisi sonuçları bozmaz.
public class DoctorSearchApiTests : IClassFixture<DoctorApiFactory>
{
    private readonly HttpClient _client;

    public DoctorSearchApiTests(DoctorApiFactory factory) => _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<ResultDoctorDto> Create(CreateDoctorDto dto) => _client.CreateOk(dto, Ct);
    private Task<PagedResult<DoctorSearchItemDto>> Search(string query) => _client.SearchOk(query, Ct);

    private static HashSet<string> Ids(PagedResult<DoctorSearchItemDto> r) => r.Items.Select(i => i.Id).ToHashSet();

    // ---- Tek tek filtreler ----

    [Fact]
    public async Task Search_WithoutFilters_ReturnsPagedShape()
    {
        var tag = NewTag();
        await Create(NewDoc("A", "Bir", tag));
        var r = await Search($"?branchIds={tag}");
        Assert.Equal(1, r.TotalCount);
        Assert.Equal(1, r.Page);
        Assert.Equal(10, r.PageSize);
        Assert.Single(r.Items);
    }

    [Fact]
    public async Task Search_ByGender()
    {
        var tag = NewTag();
        var f = await Create(NewDoc("Ayşe", "F", tag, DoctorGender.Female));
        var m = await Create(NewDoc("Ali", "M", tag, DoctorGender.Male));
        Assert.Equal([f.Id], Ids(await Search($"?branchIds={tag}&gender=Female")));
        Assert.Equal([m.Id], Ids(await Search($"?branchIds={tag}&gender=male"))); // büyük-küçük harf duyarsız
        Assert.Equal(2, (await Search($"?branchIds={tag}")).TotalCount);
    }

    [Theory]
    [InlineData("Alien")]
    [InlineData("1")]
    public async Task Search_InvalidGender_Returns400(string gender)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/doctors/search?gender={gender}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Search_ByMultipleBranches()
    {
        var tag = NewTag();
        var a = await Create(NewDoc("A", "1", tag + "a"));
        var b = await Create(NewDoc("B", "2", tag + "b"));
        await Create(NewDoc("C", "3", tag + "c"));
        var r = await Search($"?branchIds={tag}a, {tag}b,{tag}a");
        Assert.Equal(new[] { a.Id, b.Id }.ToHashSet(), Ids(r));
        Assert.Equal(2, r.TotalCount);
    }

    [Fact]
    public async Task Search_MoreThan20Branches_Returns400()
    {
        var ids = string.Join(',', Enumerable.Range(0, 21).Select(i => $"b{i}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/doctors/search?branchIds={ids}", Ct)).StatusCode);
        var twenty = string.Join(',', Enumerable.Range(0, 20).Select(i => $"b{i}"));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/doctors/search?branchIds={twenty}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Search_ByCity_MatchesAnyLocation()
    {
        var tag = NewTag();
        var both = await Create(NewDoc("A", "1", tag, locations: [Loc("Ankara-" + tag), Loc("İzmir-" + tag)]));
        await Create(NewDoc("B", "2", tag, locations: [Loc("Bursa-" + tag)]));
        Assert.Equal([both.Id], Ids(await Search($"?branchIds={tag}&city={Uri.EscapeDataString("İzmir-" + tag)}")));
        Assert.Equal([both.Id], Ids(await Search($"?branchIds={tag}&city=Ankara-{tag}")));
    }

    [Fact]
    public async Task Search_ByFeeRange_ChecksEachLocationSeparately()
    {
        var tag = NewTag();
        var cheap = await Create(NewDoc("Ucuz", "1", tag, locations: [Loc("A", 100m)]));
        var mid = await Create(NewDoc("Orta", "2", tag, locations: [Loc("A", 500m)]));
        // Konumlar 100 ve 900: min < 200, max > 600 ama HİÇBİR konum 200..600 aralığında değil.
        await Create(NewDoc("Uc", "3", tag, locations: [Loc("A", 100m), Loc("B", 900m)]));
        await Create(NewDoc("Ucretsiz", "4", tag, locations: [Loc("A")]));

        Assert.Equal([mid.Id], Ids(await Search($"?branchIds={tag}&minFee=200&maxFee=600")));
        Assert.Equal(3, (await Search($"?branchIds={tag}&minFee=100")).TotalCount); // ücretsiz doktor hariç
        var exact100 = await Search($"?branchIds={tag}&maxFee=100&minFee=100"); // cheap ve (100 + 900 konumlu) doktor
        Assert.Contains(cheap.Id, Ids(exact100));
        Assert.Equal(2, exact100.TotalCount);
        Assert.Equal(2, (await Search($"?branchIds={tag}&maxFee=500")).Items.Count(i => i.FirstName is "Ucuz" or "Orta"));
    }

    [Fact]
    public async Task Search_LocationFilters_MustBeSatisfiedByTheSameLocation()
    {
        var tag = NewTag();
        // İzmir ucuz (100), Ankara pahalı (900); Ankara Salı, İzmir Cuma çalışır.
        var d = await Create(NewDoc("Iki", "Konum", tag, locations:
        [
            Loc("İzmir-" + tag, 100m, slots: Slot(DayOfWeek.Friday)),
            Loc("Ankara-" + tag, 900m, slots: Slot(DayOfWeek.Tuesday)),
        ]));
        string Q(string extra) => $"?branchIds={tag}&{extra}";
        var ankara = "city=" + Uri.EscapeDataString("Ankara-" + tag);
        var izmir = "city=" + Uri.EscapeDataString("İzmir-" + tag);
        var tuesday = "date=2026-10-13";
        var friday = "date=2026-10-16";
        Assert.Equal(DayOfWeek.Tuesday, new DateOnly(2026, 10, 13).DayOfWeek);
        Assert.Equal(DayOfWeek.Friday, new DateOnly(2026, 10, 16).DayOfWeek);

        // Tek filtre davranışı aynı.
        Assert.Equal([d.Id], Ids(await Search(Q(ankara))));
        Assert.Equal([d.Id], Ids(await Search(Q("maxFee=500"))));
        Assert.Equal([d.Id], Ids(await Search(Q(tuesday))));

        // city + ücret: aynı konum.
        Assert.Equal(0, (await Search(Q(ankara + "&maxFee=500"))).TotalCount);   // Ankara pahalı; ucuz olan İzmir
        Assert.Equal([d.Id], Ids(await Search(Q(izmir + "&maxFee=500"))));
        Assert.Equal([d.Id], Ids(await Search(Q(ankara + "&minFee=800"))));
        Assert.Equal(0, (await Search(Q(izmir + "&minFee=800"))).TotalCount);

        // city + date: Ankara Salı, İzmir Cuma.
        Assert.Equal([d.Id], Ids(await Search(Q(ankara + "&" + tuesday))));
        Assert.Equal(0, (await Search(Q(ankara + "&" + friday))).TotalCount);
        Assert.Equal([d.Id], Ids(await Search(Q(izmir + "&" + friday))));
        Assert.Equal(0, (await Search(Q(izmir + "&" + tuesday))).TotalCount);

        // ücret + date: Cuma ucuz konum, Salı pahalı konum.
        Assert.Equal([d.Id], Ids(await Search(Q("maxFee=500&" + friday))));
        Assert.Equal(0, (await Search(Q("maxFee=500&" + tuesday))).TotalCount);

        // üçü birden.
        Assert.Equal([d.Id], Ids(await Search(Q(ankara + "&minFee=800&" + tuesday))));
        Assert.Equal(0, (await Search(Q(ankara + "&maxFee=500&" + tuesday))).TotalCount);
    }

    [Fact]
    public async Task Search_MaxPage_IsAcceptedAndReturnsEmptyPageWithCorrectTotal()
    {
        var tag = NewTag();
        await Create(NewDoc("Tek", "Doktor", tag));
        var r = await Search($"?branchIds={tag}&page=10000&pageSize=50");
        Assert.Empty(r.Items);
        Assert.Equal(1, r.TotalCount);
        Assert.Equal(10000, r.Page);
    }

    [Fact]
    public async Task Search_FeeErrors_DoNotOverwriteEachOther()
    {
        var response = await _client.GetAsync("/api/doctors/search?minFee=-5&maxFee=-10", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("minfee", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maxfee", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_InvalidFee_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/doctors/search?minFee=-1", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/doctors/search?minFee=500&maxFee=100", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/doctors/search?minFee=abc", Ct)).StatusCode);
    }

    [Fact]
    public async Task Search_ByDate_UsesWeekdayOfWeeklySchedule()
    {
        var tag = NewTag();
        var monday = new DateOnly(2026, 10, 12);
        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);
        var works = await Create(NewDoc("Calisan", "1", tag, locations: [Loc("A", 1m, slots: Slot(DayOfWeek.Monday))]));
        var otherLoc = await Create(NewDoc("IkinciKonum", "2", tag, locations:
            [Loc("A", 1m, slots: Slot(DayOfWeek.Tuesday)), Loc("B", 1m, slots: Slot(DayOfWeek.Monday, "14:00", "18:00"))]));
        await Create(NewDoc("Calismayan", "3", tag, locations: [Loc("A", 1m, slots: Slot(DayOfWeek.Tuesday))]));
        await Create(NewDoc("ProgramsizDoktor", "4", tag, locations: [Loc("A", 1m)]));

        var r = await Search($"?branchIds={tag}&date=2026-10-12");
        Assert.Equal(new[] { works.Id, otherLoc.Id }.ToHashSet(), Ids(r));
        // Aynı doktor 7 gün sonraki Pazartesi'de de çalışır (haftalık program).
        Assert.Equal(2, (await Search($"?branchIds={tag}&date=2026-10-19")).TotalCount);
        // Salı: yalnız iki doktor (Tuesday programlı olanlar).
        Assert.Equal(2, (await Search($"?branchIds={tag}&date=2026-10-13")).TotalCount);
        // Pazar: kimse çalışmıyor.
        Assert.Equal(0, (await Search($"?branchIds={tag}&date=2026-10-18")).TotalCount);
    }

    [Theory]
    [InlineData("12.10.2026")]
    [InlineData("2026-13-01")]
    [InlineData("2026-10-32")]
    [InlineData("bugun")]
    public async Task Search_InvalidDate_Returns400(string date)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/doctors/search?date={date}", Ct)).StatusCode);
    }

    // ---- Metin araması ----

    [Fact]
    public async Task Search_Text_MatchesNameServiceClinicAndDegrees_CaseInsensitive()
    {
        var tag = NewTag();
        var d = NewDoc("Zeynep" + tag, "Kaya", tag, locations: [Loc("A", clinic: "Gülümseme Diş Kliniği " + tag)]);
        d.Services = ["Implant " + tag, "Kanal Tedavisi"];
        d.Degrees = "MDS Periodontoloji " + tag;
        var target = await Create(d);
        await Create(NewDoc("Başka", "Kişi", tag));

        Assert.Equal([target.Id], Ids(await Search($"?branchIds={tag}&text={("zeynep" + tag).ToUpperInvariant()}")));  // ad, büyük harf
        Assert.Equal([target.Id], Ids(await Search($"?branchIds={tag}&text=kaya")));                                  // soyad
        Assert.Equal([target.Id], Ids(await Search($"?branchIds={tag}&text=implant {tag}")));                          // hizmet etiketi (boşluklu)
        Assert.Equal([target.Id], Ids(await Search($"?branchIds={tag}&text=kanal")));                                  // dizinin ikinci öğesi
        Assert.Equal([target.Id], Ids(await Search($"?branchIds={tag}&text=Diş Kliniği {tag}")));                      // klinik adı
        Assert.Equal([target.Id], Ids(await Search($"?branchIds={tag}&text=periodontoloji")));                         // dereceler
        Assert.Equal(0, (await Search($"?branchIds={tag}&text=yokboyleseyxyz")).TotalCount);
    }

    [Theory]
    [InlineData(".*")]
    [InlineData("(")]
    [InlineData("[a-z]+")]
    [InlineData("^")]
    [InlineData("(a+)+$")]
    [InlineData("\\")]
    [InlineData("a|b")]
    public async Task Search_Text_RegexMetaCharacters_AreLiteral_NoErrorAndNoMatchAll(string text)
    {
        var tag = NewTag();
        await Create(NewDoc("Normal", "Doktor", tag));
        var r = await Search($"?branchIds={tag}&text={Uri.EscapeDataString(text)}");
        Assert.Equal(0, r.TotalCount); // kaçırılmasaydı ".*" / "^" / "[a-z]+" hepsini eşler, "(" hata verirdi
    }

    [Fact]
    public async Task Search_Text_LiteralDotMatchesOnlyRealDot()
    {
        var tag = NewTag();
        var d = NewDoc("A", "Dot", tag);
        d.Services = ["a.b+c"];
        var withDot = await Create(d);
        var d2 = NewDoc("B", "NoDot", tag);
        d2.Services = ["axbbc"];
        await Create(d2);
        Assert.Equal([withDot.Id], Ids(await Search($"?branchIds={tag}&text={Uri.EscapeDataString("a.b+c")}")));
    }

    [Fact]
    public async Task Search_Text_TooLong_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/doctors/search?text={new string('a', 101)}", Ct)).StatusCode);
    }

    // ---- Birleşik filtreler ----

    [Fact]
    public async Task Search_CombinedFilters_AreAnded()
    {
        var tag = NewTag();
        var hit = await Create(NewDoc("Hedef", "1", tag, DoctorGender.Female, Loc("City-" + tag, 300m, slots: Slot(DayOfWeek.Monday))));
        await Create(NewDoc("Hedef", "2", tag, DoctorGender.Male, Loc("City-" + tag, 300m, slots: Slot(DayOfWeek.Monday)))); // cinsiyet tutmaz
        await Create(NewDoc("Hedef", "3", tag, DoctorGender.Female, Loc("Baska-" + tag, 300m, slots: Slot(DayOfWeek.Monday)))); // şehir tutmaz
        await Create(NewDoc("Hedef", "4", tag, DoctorGender.Female, Loc("City-" + tag, 900m, slots: Slot(DayOfWeek.Monday)))); // ücret tutmaz
        await Create(NewDoc("Hedef", "5", tag, DoctorGender.Female, Loc("City-" + tag, 300m, slots: Slot(DayOfWeek.Sunday)))); // gün tutmaz
        await Create(NewDoc("Diger", "6", tag, DoctorGender.Female, Loc("City-" + tag, 300m, slots: Slot(DayOfWeek.Monday)))); // metin tutmaz

        var r = await Search($"?branchIds={tag}&gender=Female&city=City-{tag}&minFee=200&maxFee=400&date=2026-10-12&text=hedef");
        Assert.Equal([hit.Id], Ids(r));
        Assert.Equal(1, r.TotalCount);
    }

    // ---- Sıralama ----

    [Fact]
    public async Task Search_Sort_ByNameDefault_AndByFee()
    {
        var tag = NewTag();
        await Create(NewDoc("Ali", "Cetin", tag, locations: [Loc("A", 300m)]));
        await Create(NewDoc("Veli", "Aksoy", tag, locations: [Loc("A", 100m), Loc("B", 900m)]));
        await Create(NewDoc("Can", "Bulut", tag, locations: [Loc("A", 500m)]));

        string[] Names(PagedResult<DoctorSearchItemDto> r) => [.. r.Items.Select(i => i.LastName)];
        Assert.Equal(["Aksoy", "Bulut", "Cetin"], Names(await Search($"?branchIds={tag}")));
        Assert.Equal(["Aksoy", "Bulut", "Cetin"], Names(await Search($"?branchIds={tag}&sort=name")));
        // feeAsc: en düşük konum ücretine göre (Aksoy 100, Cetin 300, Bulut 500).
        Assert.Equal(["Aksoy", "Cetin", "Bulut"], Names(await Search($"?branchIds={tag}&sort=feeAsc")));
        // feeDesc: en yüksek konum ücretine göre (Aksoy 900, Bulut 500, Cetin 300).
        Assert.Equal(["Aksoy", "Bulut", "Cetin"], Names(await Search($"?branchIds={tag}&sort=FEEDESC")));
    }

    [Fact]
    public async Task Search_InvalidSort_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/doctors/search?sort=random", Ct)).StatusCode);
    }

    // ---- Sayfalama ----

    [Fact]
    public async Task Search_Paging_TotalCountStaysConstant_AndPagesDoNotOverlap()
    {
        var tag = NewTag();
        foreach (var n in new[] { "A", "B", "C", "D", "E" }) await Create(NewDoc("Ad", n, tag));

        var p1 = await Search($"?branchIds={tag}&pageSize=2&page=1");
        var p2 = await Search($"?branchIds={tag}&pageSize=2&page=2");
        var p3 = await Search($"?branchIds={tag}&pageSize=2&page=3");
        var p4 = await Search($"?branchIds={tag}&pageSize=2&page=4");
        Assert.All(new[] { p1, p2, p3, p4 }, p => Assert.Equal(5, p.TotalCount));
        Assert.Equal([2, 2, 1, 0], new[] { p1, p2, p3, p4 }.Select(p => p.Items.Count));
        Assert.Equal([1, 2, 3, 4], new[] { p1, p2, p3, p4 }.Select(p => p.Page));
        var all = p1.Items.Concat(p2.Items).Concat(p3.Items).Select(i => i.LastName).ToList();
        Assert.Equal(["A", "B", "C", "D", "E"], all);
    }

    [Fact]
    public async Task Search_Paging_StableWhenSortKeyTies()
    {
        var tag = NewTag();
        // Aynı fiyat: Mongo tek başına sırayı garanti etmez; Id eşitlik bozucu.
        foreach (var _ in Enumerable.Range(0, 5)) await Create(NewDoc("Ayni", "Ayni", tag, locations: [Loc("A", 100m)]));
        var seen = new List<string>();
        for (var page = 1; page <= 3; page++)
            seen.AddRange((await Search($"?branchIds={tag}&sort=feeAsc&pageSize=2&page={page}")).Items.Select(i => i.Id));
        Assert.Equal(5, seen.Distinct().Count());
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=51")]
    [InlineData("pageSize=-1")]
    [InlineData("pageSize=abc")]
    [InlineData("page=0")]
    [InlineData("page=-3")]
    [InlineData("page=10001")]
    [InlineData("page=2147483647")]
    public async Task Search_OutOfRangePaging_Returns400(string query)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/doctors/search?{query}", Ct)).StatusCode);
    }

    [Fact]
    public async Task Search_PageSize50_IsAccepted()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/doctors/search?pageSize=50", Ct)).StatusCode);
    }

    // ---- Kart içeriği ----

    [Fact]
    public async Task Search_Item_HasCardFields_DerivedFees_FirstFourGallery_AndNoPhoneOrEmail()
    {
        var tag = NewTag();
        var d = NewDoc("Kart", "Doktor", tag, DoctorGender.Female,
            Loc("Muğla", 300m, currency: "USD"), Loc("Ankara", 1000m, currency: "USD"));
        d.Degrees = "BDS";
        d.Services = ["Dental Fillings", "Whitening"];
        d.Gallery = [.. Enumerable.Range(1, 6).Select(i => $"https://img.test/{i}.jpg")];
        d.ImageUrl = "https://img.test/dr.jpg";
        d.Biography = "uzun biyografi";
        await Create(d);

        var response = await _client.GetAsync($"/api/doctors/search?branchIds={tag}", Ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.False(item.TryGetProperty("phone", out _));
        Assert.False(item.TryGetProperty("email", out _));
        Assert.False(item.TryGetProperty("biography", out _));
        Assert.Equal("Female", item.GetProperty("gender").GetString());
        Assert.Equal("BDS", item.GetProperty("degrees").GetString());
        Assert.Equal(4, item.GetProperty("gallery").GetArrayLength());
        Assert.Equal(2, item.GetProperty("services").GetArrayLength());
        Assert.Equal("Muğla", item.GetProperty("primaryCity").GetString());
        Assert.Equal(300m, item.GetProperty("minFee").GetDecimal());
        Assert.Equal(1000m, item.GetProperty("maxFee").GetDecimal());
        Assert.Equal("USD", item.GetProperty("currency").GetString());
        Assert.Equal("https://img.test/dr.jpg", item.GetProperty("imageUrl").GetString());
    }

    [Fact]
    public async Task Search_Item_WithoutFees_HasNullFeeFields()
    {
        var tag = NewTag();
        await Create(NewDoc("Ucretsiz", "Doktor", tag, locations: [Loc("Ankara")]));
        var item = (await Search($"?branchIds={tag}")).Items.Single();
        Assert.Null(item.MinFee);
        Assert.Null(item.MaxFee);
        Assert.Null(item.Currency);
        Assert.Equal("Ankara", item.PrimaryCity);
    }

    // ---- Batch ----

    [Fact]
    public async Task Batch_MixedValidInvalidAndDuplicateIds()
    {
        var tag = NewTag();
        var a = await Create(NewDoc("Bir", "A", tag));
        var b = await Create(NewDoc("Iki", "B", tag));
        var ids = $"{b.Id},gecersiz,{a.Id},{b.Id}, ,123,64b7f0f0f0f0f0f0f0f0f0f0";
        var response = await _client.GetAsync($"/api/doctors/batch?ids={Uri.EscapeDataString(ids)}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<List<DoctorSummaryDto>>(Json, Ct))!;
        Assert.Equal([b.Id, a.Id], list.Select(x => x.Id)); // sıra istenen sıra, tekrar yok, bilinmeyen id yok
        Assert.Equal("A", list[1].LastName);
        Assert.Equal("Uzm. Dr.", list[0].Title);
    }

    [Fact]
    public async Task Batch_ResponseHasNoPhoneOrEmail()
    {
        var a = await Create(NewDoc("Gizli", "Gizli", "b"));
        var json = await _client.GetStringAsync($"/api/doctors/batch?ids={a.Id}", Ct);
        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement[0];
        Assert.False(item.TryGetProperty("phone", out _));
        Assert.False(item.TryGetProperty("email", out _));
        Assert.Equal(a.Id, item.GetProperty("id").GetString());
        Assert.DoesNotContain("gizli@hastane.test", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,,")]
    [InlineData("gecersiz,baska")]
    public async Task Batch_EmptyOrOnlyInvalid_ReturnsEmptyList(string ids)
    {
        var response = await _client.GetAsync($"/api/doctors/batch?ids={Uri.EscapeDataString(ids)}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<DoctorSummaryDto>>(Json, Ct))!);
    }

    [Fact]
    public async Task Batch_WithoutIdsParameter_ReturnsEmptyList()
    {
        var list = await _client.GetFromJsonAsync<List<DoctorSummaryDto>>("/api/doctors/batch", Json, Ct);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Batch_101Ids_Returns400_And100IsAccepted()
    {
        string Gen(int n) => string.Join(',', Enumerable.Range(0, n).Select(i => MongoDB.Bson.ObjectId.GenerateNewId().ToString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/doctors/batch?ids={Gen(101)}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/doctors/batch?ids={Gen(100)}", Ct)).StatusCode);
    }

    // ---- Rota çakışması ----

    [Fact]
    public async Task FixedRoutes_SearchAndBatch_AreNotCapturedByIdRoute()
    {
        var created = await Create(NewDoc("Rota", "Test", NewTag()));

        // search -> sayfalı nesne (404 değil, GetById'nin "search" id'siyle karışmadı)
        var search = await _client.GetAsync("/api/doctors/search", Ct);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using (var doc = JsonDocument.Parse(await search.Content.ReadAsStringAsync(Ct)))
            Assert.True(doc.RootElement.TryGetProperty("totalCount", out _));

        // batch -> dizi
        var batch = await _client.GetAsync($"/api/doctors/batch?ids={created.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        using (var doc = JsonDocument.Parse(await batch.Content.ReadAsStringAsync(Ct)))
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        // {id} hâlâ çalışır
        var byId = await _client.GetAsync($"/api/doctors/{created.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        using (var doc = JsonDocument.Parse(await byId.Content.ReadAsStringAsync(Ct)))
            Assert.Equal(created.Id, doc.RootElement.GetProperty("id").GetString());
    }
}
