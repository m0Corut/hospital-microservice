using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hastane.Doctor.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Hastane.Doctor.Api.Entities;
using Hastane.Doctor.Api.Validation;
using MongoDB.Bson;
using MongoDB.Driver;
using static Hastane.Doctor.Tests.TestSupport;

namespace Hastane.Doctor.Tests;

public class DoctorsApiTests : IClassFixture<DoctorApiFactory>
{
    private readonly DoctorApiFactory _factory;
    private readonly HttpClient _client;

    public DoctorsApiTests(DoctorApiFactory factory)
    {
        _factory = factory;
        // Yazma uçları Admin ister; davranış testleri Admin token'lı istemciyle koşar (yetki testleri DoctorAuthorizationTests'te).
        _client = factory.CreateClient().WithBearer(TestJwt.Create("admin-1", [HastaneRoles.Admin]));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateDoctorDto NewDoctor(string last, string branchId = "branch-1") => new()
    {
        FirstName = "Ayşe",
        LastName = last,
        Title = "Uzm. Dr.",
        Gender = DoctorGender.Female,
        BranchId = branchId,
        Biography = "Biyografi",
        Phone = "0555 000 00 00",
        Email = "ayse@hastane.test",
        Educations = [new EducationDto { School = "İstanbul Tıp", Department = "Tıp", StartYear = 2000, EndYear = 2006 }],
        Experiences =
        [
            new ExperienceDto { Place = "Şehir Hastanesi", Position = "Asistan", StartYear = 2006, EndYear = 2011 },
            new ExperienceDto { Place = "Merkez Hastane", Position = "Uzman", StartYear = 2011 },
        ],
        Awards = [new AwardDto { Title = "Yılın Doktoru", Year = 2020, Description = "Ödül" }],
        ClinicLocations = [new ClinicLocationDto { ClinicName = "Merkez Klinik", City = "Ankara", District = "Çankaya", Floor = "3" }],
    };

    private Task<ResultDoctorDto> CreateAsync(CreateDoctorDto dto) => _client.CreateOk(dto, Ct);

    [Fact]
    public async Task Create_ThenList_ContainsDoctor_WithoutPhoneOrEmail()
    {
        var created = await CreateAsync(NewDoctor("Liste"));
        var json = await _client.GetStringAsync("/api/doctors", Ct);
        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == created.Id);
        Assert.Equal("Liste", item.GetProperty("lastName").GetString());
        Assert.Equal("Female", item.GetProperty("gender").GetString());
        Assert.False(item.TryGetProperty("phone", out _));
        Assert.False(item.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task GetById_ReturnsNestedLists()
    {
        var created = await CreateAsync(NewDoctor("Detay"));
        var dto = await _client.GetFromJsonAsync<GetByIdDoctorDto>($"/api/doctors/{created.Id}", Json, Ct);
        Assert.Equal("ayse@hastane.test", dto!.Email);
        Assert.Equal("0555 000 00 00", dto.Phone);
        Assert.Equal("İstanbul Tıp", Assert.Single(dto.Educations).School);
        Assert.Equal(2, dto.Experiences.Count);
        Assert.Null(dto.Experiences[1].EndYear);
        Assert.Equal("Yılın Doktoru", Assert.Single(dto.Awards).Title);
        var loc = Assert.Single(dto.ClinicLocations);
        Assert.Equal("Çankaya", loc.District);
        Assert.Equal("3", loc.Floor);
    }

    [Fact]
    public async Task NewFields_RoundTrip_AndAreStoredAsExpectedBsonTypes()
    {
        var dto = NewDoctor("Alanlar");
        dto.Degrees = "MDS - Periodontology and Oral Implantology, BDS";
        dto.Services = ["Dental Fillings", "Whitening"];
        dto.Gallery = ["https://img.test/1.jpg", "http://img.test/2.jpg"];
        dto.SocialLinks = new SocialLinksDto
        {
            Facebook = "https://facebook.com/dr",
            X = "https://x.com/dr",
            Instagram = "https://instagram.com/dr",
            LinkedIn = "https://linkedin.com/in/dr",
            Pinterest = "https://pinterest.com/dr",
            YouTube = "https://youtube.com/@dr",
        };
        dto.ClinicLocations =
        [
            Loc("İstanbul", 199.99m, "Klinik A", "USD", Slot(DayOfWeek.Monday, "09:00", "12:00", 20), Slot(DayOfWeek.Monday, "13:00", "17:30", 45)),
            Loc("Ankara", 0m, "Klinik B"),
        ];
        var created = await CreateAsync(dto);

        var got = (await _client.GetFromJsonAsync<GetByIdDoctorDto>($"/api/doctors/{created.Id}", Json, Ct))!;
        Assert.Equal(DoctorGender.Female, got.Gender);
        Assert.Equal(dto.Degrees, got.Degrees);
        Assert.Equal(dto.Services, got.Services);
        Assert.Equal(dto.Gallery, got.Gallery);
        Assert.Equal("https://linkedin.com/in/dr", got.SocialLinks.LinkedIn);
        Assert.Equal("https://youtube.com/@dr", got.SocialLinks.YouTube);
        var loc = got.ClinicLocations[0];
        Assert.Equal(199.99m, loc.HourlyFee);
        Assert.Equal("USD", loc.Currency);
        Assert.Equal(2, loc.Schedule.Count);
        Assert.Equal((DayOfWeek.Monday, "13:00", "17:30", 45), (loc.Schedule[1].DayOfWeek, loc.Schedule[1].StartTime, loc.Schedule[1].EndTime, loc.Schedule[1].SlotMinutes));
        // Türetilmiş: ücretleri olan konumlar USD (ilk ücretli) -> yalnız 199.99; ikinci konum TRY olduğu için hariç.
        Assert.Equal((199.99m, 199.99m, "USD", "İstanbul"), (got.MinFee, got.MaxFee, got.Currency, got.PrimaryCity));

        // Ham belge: Decimal128 ücret, metin enum'lar, iç içe SocialLinks, türetilmiş alan YOK.
        var raw = await _factory.RawCollection.Find(new BsonDocument("_id", ObjectId.Parse(created.Id))).SingleAsync(Ct);
        Assert.Equal(BsonType.String, raw["Gender"].BsonType);
        Assert.Equal("Female", raw["Gender"].AsString);
        var rawLoc = raw["ClinicLocations"].AsBsonArray[0].AsBsonDocument;
        Assert.Equal(BsonType.Decimal128, rawLoc["HourlyFee"].BsonType);
        Assert.Equal("Monday", rawLoc["Schedule"].AsBsonArray[0]["DayOfWeek"].AsString);
        Assert.Equal("https://facebook.com/dr", raw["SocialLinks"]["Facebook"].AsString);
        Assert.False(raw.Contains("MinFee"));
    }

    [Fact]
    public async Task Indexes_AreCreatedAtStartup()
    {
        _ = await _client.GetAsync("/api/doctors", Ct);
        using var cursor = await _factory.RawCollection.Indexes.ListAsync(Ct);
        var names = (await cursor.ToListAsync(Ct)).Select(i => i["name"].AsString).ToList();
        Assert.Contains("ix_branch", names);
        Assert.Contains("ix_gender", names);
        Assert.Contains("ix_city", names);
    }

    [Fact]
    public async Task List_FiltersByBranchId()
    {
        var a = await CreateAsync(NewDoctor("A", "branch-filter-x"));
        await CreateAsync(NewDoctor("B", "branch-filter-y"));
        var list = await _client.GetFromJsonAsync<List<ResultDoctorDto>>("/api/doctors?branchId=branch-filter-x", Json, Ct);
        Assert.Equal(a.Id, Assert.Single(list!).Id);
    }

    [Fact]
    public async Task Update_ChangesDoctorAndNestedLists()
    {
        var created = await CreateAsync(NewDoctor("Eski"));
        var update = new UpdateDoctorDto
        {
            Id = created.Id,
            FirstName = "Mehmet",
            LastName = "Yeni",
            Gender = DoctorGender.Male,
            BranchId = "branch-2",
            Awards = [new AwardDto { Title = "A", Year = 2021 }, new AwardDto { Title = "B", Year = 2022 }],
        };
        var put = await _client.PutAsJsonAsync($"/api/doctors/{created.Id}", update, Json, Ct);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var dto = await _client.GetFromJsonAsync<GetByIdDoctorDto>($"/api/doctors/{created.Id}", Json, Ct);
        Assert.Equal("Yeni", dto!.LastName);
        Assert.Equal(DoctorGender.Male, dto.Gender);
        Assert.Equal("branch-2", dto.BranchId);
        Assert.Equal(2, dto.Awards.Count);
        Assert.Empty(dto.Educations);
    }

    [Fact]
    public async Task Update_WithOverlappingSchedule_Returns400()
    {
        var created = await CreateAsync(NewDoctor("Guncel"));
        var update = new UpdateDoctorDto
        {
            Id = created.Id,
            FirstName = "x",
            LastName = "y",
            Gender = DoctorGender.Other,
            BranchId = "b",
            ClinicLocations = [Loc("Ankara", 1m, "K", "TRY", Slot(DayOfWeek.Friday, "09:00", "12:00"), Slot(DayOfWeek.Friday, "11:00", "14:00"))],
        };
        var put = await _client.PutAsJsonAsync($"/api/doctors/{created.Id}", update, Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Delete_ThenGet_Returns404()
    {
        var created = await CreateAsync(NewDoctor("Silinecek"));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/doctors/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/doctors/{created.Id}", Ct)).StatusCode);
    }

    private static UpdateDoctorDto MinimalUpdate(string id) =>
        new() { Id = id, FirstName = "x", LastName = "y", Gender = DoctorGender.Male, BranchId = "b" };

    [Theory]
    [InlineData("64b7f0f0f0f0f0f0f0f0f0f0")]
    [InlineData("gecersiz-id")]
    public async Task UnknownOrMalformedId_Returns404(string id)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/doctors/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/doctors/{id}", Ct)).StatusCode);
        var put = await _client.PutAsJsonAsync($"/api/doctors/{id}", MinimalUpdate(id), Json, Ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Put_WithMismatchedId_Returns400()
    {
        var created = await CreateAsync(NewDoctor("Uyumsuz"));
        var put = await _client.PutAsJsonAsync($"/api/doctors/{created.Id}", MinimalUpdate("64b7f0f0f0f0f0f0f0f0f0f0"), Json, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    // ---- Doğrulama (DTO katmanı: HTTP 400) ----

    private static CreateDoctorDto WithLocation(params ClinicLocationDto[] locations)
    {
        var d = NewDoctor("Dogrulama");
        d.ClinicLocations = [.. locations];
        return d;
    }

    private static CreateDoctorDto WithSocial(Action<SocialLinksDto> set)
    {
        var d = NewDoctor("Dogrulama");
        set(d.SocialLinks);
        return d;
    }

    private static CreateDoctorDto Mutate(Action<CreateDoctorDto> set)
    {
        var d = NewDoctor("Dogrulama");
        set(d);
        return d;
    }

    public static IEnumerable<object[]> InvalidDoctors()
    {
        var cases = new Dictionary<string, CreateDoctorDto>
        {
            ["galeri geçersiz url"] = Mutate(d => d.Gallery = ["not a url"]),
            ["galeri ftp"] = Mutate(d => d.Gallery = ["ftp://x.test/a.jpg"]),
            ["galeri 11 öğe"] = Mutate(d => d.Gallery = [.. Enumerable.Range(0, 11).Select(i => $"https://x.test/{i}.jpg")]),
            ["galeri öğe 501 karakter"] = Mutate(d => d.Gallery = ["https://x.test/" + new string('a', 490)]),
            ["sosyal geçersiz"] = WithSocial(s => s.Facebook = "facebook.com/dr"),
            ["sosyal javascript"] = WithSocial(s => s.X = "javascript:alert(1)"),
            ["sosyal 301 karakter"] = WithSocial(s => s.Pinterest = "https://p.test/" + new string('a', 290)),
            ["hizmet 21 öğe"] = Mutate(d => d.Services = [.. Enumerable.Range(0, 21).Select(i => $"h{i}")]),
            ["hizmet 81 karakter"] = Mutate(d => d.Services = [new string('a', 81)]),
            ["dereceler 301 karakter"] = Mutate(d => d.Degrees = new string('a', 301)),
            ["negatif ücret"] = WithLocation(Loc("A", -1m)),
            ["para birimi küçük harf"] = WithLocation(Loc("A", 1m, currency: "try")),
            ["para birimi 4 harf"] = WithLocation(Loc("A", 1m, currency: "TRYX")),
            ["saat 24:00"] = WithLocation(Loc("A", 1m, slots: Slot(DayOfWeek.Monday, "09:00", "24:00"))),
            ["saat 9:00"] = WithLocation(Loc("A", 1m, slots: Slot(DayOfWeek.Monday, "9:00", "12:00"))),
            ["saat 09:60"] = WithLocation(Loc("A", 1m, slots: Slot(DayOfWeek.Monday, "09:60", "12:00"))),
            ["bitiş başlangıca eşit"] = WithLocation(Loc("A", 1m, slots: Slot(DayOfWeek.Monday, "09:00", "09:00"))),
            ["bitiş başlangıçtan önce"] = WithLocation(Loc("A", 1m, slots: Slot(DayOfWeek.Monday, "12:00", "09:00"))),
            ["slot 4 dk"] = WithLocation(Loc("A", 1m, slots: Slot(DayOfWeek.Monday, "09:00", "12:00", 4))),
            ["slot 121 dk"] = WithLocation(Loc("A", 1m, slots: Slot(DayOfWeek.Monday, "09:00", "12:00", 121))),
            ["çakışan program"] = WithLocation(Loc("A", 1m, slots: [Slot(DayOfWeek.Tuesday, "09:00", "12:00"), Slot(DayOfWeek.Tuesday, "11:59", "15:00")])),
            ["iç içe program"] = WithLocation(Loc("A", 1m, slots: [Slot(DayOfWeek.Tuesday, "09:00", "17:00"), Slot(DayOfWeek.Tuesday, "10:00", "11:00")])),
            ["22 program"] = WithLocation(Loc("A", 1m, slots: [.. Enumerable.Range(0, 22).Select(i => Slot((DayOfWeek)(i % 7), $"{i / 7 * 4 + 6:00}:00", $"{i / 7 * 4 + 8:00}:00"))])),
        };
        foreach (var (name, dto) in cases) yield return [name, dto];
    }

    [Theory]
    [MemberData(nameof(InvalidDoctors))]
    public async Task Create_WithInvalidInput_Returns400(string name, CreateDoctorDto dto)
    {
        var response = await _client.PostAsJsonAsync("/api/doctors", dto, Json, Ct);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: {response.StatusCode}");
    }

    [Fact]
    public async Task Create_With21ScheduleSlots_Boundary_21IsAccepted_22Rejected()
    {
        // Haftada 21 farklı aralık: 7 gün x 3 aralık. Sınırda 21 geçer.
        var slots = Enumerable.Range(0, 21).Select(i => Slot((DayOfWeek)(i % 7), $"{i / 7 * 4 + 6:00}:00", $"{i / 7 * 4 + 8:00}:00")).ToArray();
        var ok = await _client.PostAsJsonAsync("/api/doctors", WithLocation(Loc("A", 1m, slots: slots)), Json, Ct);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Create_TouchingSlots_AreAllowed()
    {
        var dto = WithLocation(Loc("A", 1m, slots: [Slot(DayOfWeek.Monday, "09:00", "12:00"), Slot(DayOfWeek.Monday, "12:00", "15:00"), Slot(DayOfWeek.Tuesday, "09:00", "12:00")]));
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/doctors", dto, Json, Ct)).StatusCode);
    }

    [Fact]
    public async Task Create_SameDayOverlapIsPerLocation_NotAcrossLocations()
    {
        var dto = WithLocation(
            Loc("A", 1m, slots: Slot(DayOfWeek.Monday, "09:00", "12:00")),
            Loc("B", 1m, slots: Slot(DayOfWeek.Monday, "10:00", "13:00")));
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/doctors", dto, Json, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("""{"firstName":"a","lastName":"b","branchId":"x"}""")] // Gender yok
    [InlineData("""{"firstName":"a","lastName":"b","branchId":"x","gender":"Alien"}""")]
    [InlineData("""{"firstName":"a","lastName":"b","branchId":"x","gender":1}""")] // sayı kabul edilmez
    public async Task Create_WithMissingOrInvalidGender_Returns400(string body)
    {
        var response = await _client.PostAsync("/api/doctors", new StringContent(body, Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithOnlyRequiredFields_Succeeds_AndDefaultsAreEmpty()
    {
        var response = await _client.PostAsync("/api/doctors",
            new StringContent("""{"firstName":"a","lastName":"b","branchId":"x","gender":"Other"}""", Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResultDoctorDto>(Json, Ct))!;
        var got = (await _client.GetFromJsonAsync<GetByIdDoctorDto>($"/api/doctors/{created.Id}", Json, Ct))!;
        Assert.Equal(DoctorGender.Other, got.Gender);
        Assert.Empty(got.Services);
        Assert.Empty(got.Gallery);
        Assert.Null(got.SocialLinks.Facebook);
        Assert.Null(got.MinFee);
    }

    // ---- Doğrulama (servis katmanı: DTO öznitelikleri atlansa bile) ----

    [Fact]
    public void ServiceLayerValidator_RejectsWhatDtoLayerWouldReject()
    {
        // DTO nesnesini doğrudan veriyoruz: model bağlama / öznitelik katmanı hiç çalışmıyor.
        var overlapping = WithLocation(Loc("A", 1m, slots: [Slot(DayOfWeek.Monday, "09:00", "12:00"), Slot(DayOfWeek.Monday, "11:00", "13:00")]));
        var ex = Assert.Throws<DoctorValidationException>(() => DoctorInputValidator.ValidateOrThrow(overlapping));
        Assert.Contains(ex.Errors, e => e.Key.Contains("Schedule") && e.Value.Any(m => m.Contains("çakışıyor")));

        Assert.Throws<DoctorValidationException>(() => DoctorInputValidator.ValidateOrThrow(WithLocation(Loc("A", -5m))));
        Assert.Throws<DoctorValidationException>(() => DoctorInputValidator.ValidateOrThrow(Mutate(d => d.Gallery = ["nope"])));
        Assert.Throws<DoctorValidationException>(() => DoctorInputValidator.ValidateOrThrow(Mutate(d => d.Gender = null)));
        Assert.Throws<DoctorValidationException>(() => DoctorInputValidator.ValidateOrThrow(Mutate(d => d.Gender = (DoctorGender)99)));
        Assert.Throws<DoctorValidationException>(() => DoctorInputValidator.ValidateOrThrow(
            WithLocation(Loc("A", 1m, slots: [.. Enumerable.Range(0, 22).Select(_ => Slot(DayOfWeek.Monday))]))));

        DoctorInputValidator.ValidateOrThrow(NewDoctor("Gecerli")); // geçerli girdi fırlatmaz
    }
}
