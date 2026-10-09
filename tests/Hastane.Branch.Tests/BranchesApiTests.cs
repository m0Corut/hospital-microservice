using System.Net;
using System.Net.Http.Json;
using Hastane.Branch.Api.Dtos;

namespace Hastane.Branch.Tests;

public class BranchesApiTests : IClassFixture<BranchApiFactory>
{
    private readonly HttpClient _client;

    public BranchesApiTests(BranchApiFactory factory) => _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ResultBranchDto> CreateAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/branches",
            new CreateBranchDto { Name = name, Description = "açıklama", Icon = "fa-heart-pulse" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var dto = await response.Content.ReadFromJsonAsync<ResultBranchDto>(Ct);
        return dto!;
    }

    [Fact]
    public async Task Create_ThenList_ContainsBranch()
    {
        var created = await CreateAsync("Kardiyoloji");
        var list = await _client.GetFromJsonAsync<List<ResultBranchDto>>("/api/branches", Ct);
        Assert.Contains(list!, b => b.Id == created.Id && b.Name == "Kardiyoloji");
    }

    [Fact]
    public async Task GetById_ReturnsBranch()
    {
        var created = await CreateAsync("Nöroloji");
        var dto = await _client.GetFromJsonAsync<GetByIdBranchDto>($"/api/branches/{created.Id}", Ct);
        Assert.Equal("Nöroloji", dto!.Name);
        Assert.Equal("fa-heart-pulse", dto.Icon);
    }

    [Fact]
    public async Task Update_ChangesBranch()
    {
        var created = await CreateAsync("Eski");
        var put = await _client.PutAsJsonAsync($"/api/branches/{created.Id}",
            new UpdateBranchDto { Id = created.Id, Name = "Yeni" }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var dto = await _client.GetFromJsonAsync<GetByIdBranchDto>($"/api/branches/{created.Id}", Ct);
        Assert.Equal("Yeni", dto!.Name);
    }

    [Fact]
    public async Task Delete_ThenGet_Returns404()
    {
        var created = await CreateAsync("Silinecek");
        var del = await _client.DeleteAsync($"/api/branches/{created.Id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        var get = await _client.GetAsync($"/api/branches/{created.Id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Theory]
    [InlineData("64b7f0f0f0f0f0f0f0f0f0f0")]
    [InlineData("gecersiz-id")]
    public async Task UnknownOrMalformedId_Returns404(string id)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/branches/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/branches/{id}", Ct)).StatusCode);
        var put = await _client.PutAsJsonAsync($"/api/branches/{id}", new UpdateBranchDto { Id = id, Name = "x" }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Put_WithMismatchedId_Returns400()
    {
        var created = await CreateAsync("Ortopedi");
        var put = await _client.PutAsJsonAsync($"/api/branches/{created.Id}",
            new UpdateBranchDto { Id = "64b7f0f0f0f0f0f0f0f0f0f0", Name = "x" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }
}
