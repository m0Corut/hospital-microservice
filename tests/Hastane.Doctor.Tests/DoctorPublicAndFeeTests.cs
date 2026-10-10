using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Entities;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using static Hastane.Doctor.Tests.TestSupport;

namespace Hastane.Doctor.Tests;

// Gece N3b: GET api/doctors/{id}/public (anonim, telefon/e-posta yok) ve arama ücret tutarlılığı (HourlyFee > 0).
public class DoctorPublicAndFeeTests : IClassFixture<DoctorApiFactory>
{
    private const string UnknownId = "64b7f0f0f0f0f0f0f0f0f0f0";

    private readonly HttpClient _admin;
    private readonly HttpClient _anonymous;
    private readonly HttpClient _patient;

    public DoctorPublicAndFeeTests(DoctorApiFactory factory)
    {
        _admin = factory.CreateClient().WithBearer(TestJwt.Create("admin-1", [HastaneRoles.Admin]));
        _anonymous = factory.CreateClient();
        _patient = factory.CreateClient().WithBearer(TestJwt.Create("patient-1", [HastaneRoles.Patient]));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---------- public profil ----------

    [Fact]
    public async Task Public_Anonymous_Returns200_WithProfileFields()
    {
        var dto = NewDoc("Ayşe", "Açık", "b", DoctorGender.Female, Loc("Ankara", 250m, "Merkez"));
        dto.Biography = "Biyografi";
        var created = await _admin.CreateOk(dto, Ct);

        var response = await _anonymous.GetAsync($"/api/doctors/{created.Id}/public", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var got = (await response.Content.ReadFromJsonAsync<PublicDoctorDto>(Json, Ct))!;
        Assert.Equal((created.Id, "Ayşe", "Açık", "Biyografi"), (got.Id, got.FirstName, got.LastName, got.Biography));
        Assert.Equal((250m, "TRY", "Ankara"), (got.MinFee, got.Currency, got.PrimaryCity));
        Assert.Single(got.ClinicLocations);
    }

    [Fact]
    public async Task Public_Response_HasNoPhoneOrEmail_AnywhereInJson()
    {
        // NewDoc telefon "0555 000 00 00" ve e-posta "gizli@hastane.test" ile kaydeder.
        var created = await _admin.CreateOk(NewDoc("Gizli", "Alan", "b", DoctorGender.Male, Loc("Ankara", 100m)), Ct);

        var text = await _anonymous.GetStringAsync($"/api/doctors/{created.Id}/public", Ct);

        using var json = JsonDocument.Parse(text);
        var names = new List<string>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject()) { names.Add(p.Name); Walk(p.Value); }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var i in e.EnumerateArray()) Walk(i);
        }
        Walk(json.RootElement);
        Assert.DoesNotContain(names, n => n.Equals("phone", StringComparison.OrdinalIgnoreCase)
            || n.Equals("email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("0555", text);
        Assert.DoesNotContain("gizli@hastane.test", text);
    }

    [Fact]
    public void Public_Dto_PropertySet_IsPinned_AndHasNoPhoneEmail()
    {
        // Sızıntı bekçisi: yeni bir hassas alan GetByIdDoctorDto'ya eklenirse Public'e de eklenmiş olmasın diye
        // Public'in alan listesi tek tek sabitlenir.
        var names = typeof(PublicDoctorDto).GetProperties().Select(p => p.Name).Order().ToList();
        Assert.Equal(
            ["Awards", "Biography", "BranchId", "ClinicLocations", "Currency", "Degrees", "Educations", "Experiences",
             "FirstName", "Gallery", "Gender", "Id", "ImageUrl", "LastName", "MaxFee", "MinFee", "PrimaryCity",
             "Services", "SocialLinks", "Title"],
            names);
    }

    [Fact]
    public async Task GetById_StillAuthenticated_AndStillHasPhoneEmail()
    {
        var created = await _admin.CreateOk(NewDoc("Hâlâ", "Korumalı", "b"), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync($"/api/doctors/{created.Id}", Ct)).StatusCode);
        var got = (await _patient.GetFromJsonAsync<GetByIdDoctorDto>($"/api/doctors/{created.Id}", Json, Ct))!;
        Assert.Equal("0555 000 00 00", got.Phone);
        Assert.Equal("gizli@hastane.test", got.Email);
    }

    [Theory]
    [InlineData(UnknownId)]
    [InlineData("bozuk-id")]
    public async Task Public_UnknownOrMalformedId_Returns404(string id)
        => Assert.Equal(HttpStatusCode.NotFound, (await _anonymous.GetAsync($"/api/doctors/{id}/public", Ct)).StatusCode);

    [Fact]
    public async Task Public_WithGarbageToken_StillReturns200_BecauseEndpointIsAnonymous()
    {
        // AllowAnonymous uçta bozuk token: JwtBearer token'ı doğrulayamaz ama uç anonim olduğu için yetki başarısız olmaz.
        var created = await _admin.CreateOk(NewDoc("Token", "Bozuk", "b"), Ct);
        var client = _anonymous;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/doctors/{created.Id}/public");
        request.Headers.Authorization = new("Bearer", "bozuk.token.degeri");
        var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- arama: ücret tutarlılığı ----------

    private static string[] Names(PagedResult<DoctorSearchItemDto> r) => [.. r.Items.Select(i => i.LastName)];

    private Task<PagedResult<DoctorSearchItemDto>> Search(string q) => _anonymous.SearchOk(q, Ct);

    [Fact]
    public async Task FeeAsc_ZeroAndPositiveDoctor_SortsAfterCheaperPositiveDoctor_AndFeelessGoLast()
    {
        var tag = NewTag();
        await _admin.CreateOk(NewDoc("A", "Ucretsiz", tag, locations: [Loc("A", null)]), Ct);
        await _admin.CreateOk(NewDoc("B", "SifirVeIki", tag, locations: [Loc("A", 0m), Loc("B", 200m)]), Ct);
        await _admin.CreateOk(NewDoc("C", "YuzElli", tag, locations: [Loc("A", 150m)]), Ct);
        await _admin.CreateOk(NewDoc("D", "KonumYok", tag), Ct);

        // [0, 200] doktorunun ücreti 200: 150'lik doktordan SONRA. Ücreti olmayanlar (null / konumsuz) en sonda.
        var asc = Names(await Search($"?branchIds={tag}&sort=feeAsc"));
        Assert.Equal(["YuzElli", "SifirVeIki"], asc[..2]);
        Assert.Equivalent(new[] { "Ucretsiz", "KonumYok" }, asc[2..]);

        var desc = Names(await Search($"?branchIds={tag}&sort=feeDesc"));
        Assert.Equal(["SifirVeIki", "YuzElli"], desc[..2]);
        Assert.Equivalent(new[] { "Ucretsiz", "KonumYok" }, desc[2..]);
    }

    [Fact]
    public async Task FeeSort_PagingIsStable_AndTotalCountIncludesFeeless()
    {
        var tag = NewTag();
        for (var i = 0; i < 5; i++)
            await _admin.CreateOk(NewDoc("F", $"Ucretli{i}", tag, locations: [Loc("A", 100m + i)]), Ct);
        await _admin.CreateOk(NewDoc("F", "Ucretsiz", tag, locations: [Loc("A", 0m)]), Ct);

        var seen = new List<string>();
        foreach (var page in new[] { 1, 2, 3 })
        {
            var r = await Search($"?branchIds={tag}&sort=feeAsc&pageSize=2&page={page}");
            Assert.Equal(6, r.TotalCount);
            seen.AddRange(Names(r));
        }

        Assert.Equal(["Ucretli0", "Ucretli1", "Ucretli2", "Ucretli3", "Ucretli4", "Ucretsiz"], seen);
    }

    [Fact]
    public async Task FeeSort_ResultCards_StillHaveNoPhoneOrEmail()
    {
        var tag = NewTag();
        await _admin.CreateOk(NewDoc("K", "Kart", tag, locations: [Loc("A", 100m)]), Ct);
        var text = await _anonymous.GetStringAsync($"/api/doctors/search?branchIds={tag}&sort=feeAsc", Ct);
        Assert.DoesNotContain("phone", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__", text);
    }

    [Fact]
    public async Task FeeFilter_IgnoresZeroFeeLocations()
    {
        var tag = NewTag();
        await _admin.CreateOk(NewDoc("A", "SadeceSifir", tag, locations: [Loc("A", 0m)]), Ct);
        await _admin.CreateOk(NewDoc("B", "Bos", tag, locations: [Loc("A", null)]), Ct);
        await _admin.CreateOk(NewDoc("C", "IkiYuz", tag, locations: [Loc("A", 0m), Loc("B", 200m)]), Ct);

        // maxFee=300: 0'lık konum "ücret" değil, bu yüzden yalnız 200'lük konumlu doktor eşleşir.
        Assert.Equal(["IkiYuz"], Names(await Search($"?branchIds={tag}&maxFee=300")));
        // minFee=0 bile ücretsiz doktorları getirmez: "ücreti tanımlı olanlar, en az 0".
        Assert.Equal(["IkiYuz"], Names(await Search($"?branchIds={tag}&minFee=0")));
        Assert.Equal(["IkiYuz"], Names(await Search($"?branchIds={tag}&minFee=150&maxFee=250")));
        Assert.Empty((await Search($"?branchIds={tag}&minFee=250")).Items);
    }

    [Fact]
    public async Task NoFeeFilter_KeepsFeelessDoctorsInList()
    {
        var tag = NewTag();
        await _admin.CreateOk(NewDoc("A", "SadeceSifir", tag, locations: [Loc("A", 0m)]), Ct);
        await _admin.CreateOk(NewDoc("B", "Bos", tag, locations: [Loc("A", null)]), Ct);
        await _admin.CreateOk(NewDoc("C", "IkiYuz", tag, locations: [Loc("A", 200m)]), Ct);

        var r = await Search($"?branchIds={tag}");
        Assert.Equal(3, r.TotalCount);
    }

    [Fact]
    public async Task FeeFilter_AndCity_StillMustBeSatisfiedBySameLocation()
    {
        var tag = NewTag();
        // Ankara konumu ücretsiz (0), İzmir konumu 200: city=Ankara&maxFee=500 AYNI konumda sağlanmalı -> eşleşmez.
        await _admin.CreateOk(NewDoc("A", "Karisik", tag, locations: [Loc("Ankara-" + tag, 0m), Loc("Izmir-" + tag, 200m)]), Ct);
        Assert.Empty((await Search($"?branchIds={tag}&city=Ankara-{tag}&maxFee=500")).Items);
        Assert.Single((await Search($"?branchIds={tag}&city=Izmir-{tag}&maxFee=500")).Items);
    }
}
