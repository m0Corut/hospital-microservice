using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hastane.Doctor.Api.Dtos;

namespace Hastane.Doctor.Tests;

public class DoctorsApiTests : IClassFixture<DoctorApiFactory>
{
    private readonly HttpClient _client;

    public DoctorsApiTests(DoctorApiFactory factory) => _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateDoctorDto NewDoctor(string last, string branchId = "branch-1") => new()
    {
        FirstName = "Ayşe",
        LastName = last,
        Title = "Uzm. Dr.",
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

    private async Task<ResultDoctorDto> CreateAsync(CreateDoctorDto dto)
    {
        var response = await _client.PostAsJsonAsync("/api/doctors", dto, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<ResultDoctorDto>(Ct))!;
    }

    [Fact]
    public async Task Create_ThenList_ContainsDoctor_WithoutPhoneOrEmail()
    {
        var created = await CreateAsync(NewDoctor("Liste"));
        var json = await _client.GetStringAsync("/api/doctors", Ct);
        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == created.Id);
        Assert.Equal("Liste", item.GetProperty("lastName").GetString());
        Assert.False(item.TryGetProperty("phone", out _));
        Assert.False(item.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task GetById_ReturnsNestedLists()
    {
        var created = await CreateAsync(NewDoctor("Detay"));
        var dto = await _client.GetFromJsonAsync<GetByIdDoctorDto>($"/api/doctors/{created.Id}", Ct);
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
    public async Task List_FiltersByBranchId()
    {
        var a = await CreateAsync(NewDoctor("A", "branch-filter-x"));
        await CreateAsync(NewDoctor("B", "branch-filter-y"));
        var list = await _client.GetFromJsonAsync<List<ResultDoctorDto>>("/api/doctors?branchId=branch-filter-x", Ct);
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
            BranchId = "branch-2",
            Awards = [new AwardDto { Title = "A", Year = 2021 }, new AwardDto { Title = "B", Year = 2022 }],
        };
        var put = await _client.PutAsJsonAsync($"/api/doctors/{created.Id}", update, Ct);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var dto = await _client.GetFromJsonAsync<GetByIdDoctorDto>($"/api/doctors/{created.Id}", Ct);
        Assert.Equal("Yeni", dto!.LastName);
        Assert.Equal("branch-2", dto.BranchId);
        Assert.Equal(2, dto.Awards.Count);
        Assert.Empty(dto.Educations);
    }

    [Fact]
    public async Task Delete_ThenGet_Returns404()
    {
        var created = await CreateAsync(NewDoctor("Silinecek"));
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/doctors/{created.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/doctors/{created.Id}", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("64b7f0f0f0f0f0f0f0f0f0f0")]
    [InlineData("gecersiz-id")]
    public async Task UnknownOrMalformedId_Returns404(string id)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/doctors/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/doctors/{id}", Ct)).StatusCode);
        var put = await _client.PutAsJsonAsync($"/api/doctors/{id}",
            new UpdateDoctorDto { Id = id, FirstName = "x", LastName = "y", BranchId = "b" }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Put_WithMismatchedId_Returns400()
    {
        var created = await CreateAsync(NewDoctor("Uyumsuz"));
        var put = await _client.PutAsJsonAsync($"/api/doctors/{created.Id}",
            new UpdateDoctorDto { Id = "64b7f0f0f0f0f0f0f0f0f0f0", FirstName = "x", LastName = "y", BranchId = "b" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }
}
