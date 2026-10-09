using System.Net;
using System.Net.Http.Json;
using Hastane.Identity.Api.Data;
using Hastane.Identity.Api.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Identity.Tests;

public class UsersApiTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _client;

    public UsersApiTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<UserSummaryDto> RegisterAsync(string firstName)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new { firstName, lastName = "Test", email = $"{Guid.NewGuid():N}@example.com", password = "Passw0rd1" }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!;
    }

    [Fact]
    public async Task Roles_AreSeeded_AtStartup()
    {
        // İstemci oluşturuldu => uygulama açıldı => tohumlama çalıştı.
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Doctor", "Patient" })
            Assert.True(await roleManager.RoleExistsAsync(role), $"{role} rolü yok");
    }

    [Fact]
    public async Task RoleSeeder_IsIdempotent()
    {
        await RoleSeeder.SeedAsync(_factory.Services);
        await RoleSeeder.SeedAsync(_factory.Services);

        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        Assert.Equal(3, roleManager.Roles.Count());
    }

    [Fact]
    public async Task GetById_ReturnsSummaryWithoutSecrets()
    {
        var user = await RegisterAsync("Mehmet");
        var response = await _client.GetAsync($"/api/users/{user.Id}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var forbidden in new[] { "passwordhash", "securitystamp", "phonenumber", "lockout" })
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);

        var dto = await response.Content.ReadFromJsonAsync<UserSummaryDto>(Ct);
        Assert.Equal("Mehmet", dto!.FirstName);
        Assert.Equal(["Patient"], dto.Roles);
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var response = await _client.GetAsync($"/api/users/{Guid.NewGuid()}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("'; DROP TABLE AspNetUsers;--")]
    public async Task GetById_MalformedId_Returns404(string id)
    {
        var response = await _client.GetAsync($"/api/users/{Uri.EscapeDataString(id)}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetByIds_ReturnsOnlyRequestedUsers()
    {
        var a = await RegisterAsync("Ali");
        var b = await RegisterAsync("Berk");
        await RegisterAsync("Can"); // istenmedi

        var list = await _client.GetFromJsonAsync<List<UserSummaryDto>>(
            $"/api/users?ids={a.Id},{b.Id},{Guid.NewGuid()},bozuk-id", Ct);

        Assert.Equal(2, list!.Count);
        Assert.Contains(list, u => u.Id == a.Id && u.FirstName == "Ali");
        Assert.Contains(list, u => u.Id == b.Id && u.FirstName == "Berk");
        Assert.All(list, u => Assert.Equal(["Patient"], u.Roles));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(",,,")]
    public async Task GetByIds_EmptyOrNoIds_ReturnsEmptyList(string ids)
    {
        var response = await _client.GetAsync($"/api/users?ids={Uri.EscapeDataString(ids)}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<UserSummaryDto>>(Ct))!);
    }

    [Fact]
    public async Task GetByIds_WithoutQuery_ReturnsEmptyList()
    {
        var list = await _client.GetFromJsonAsync<List<UserSummaryDto>>("/api/users", Ct);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task GetByIds_TooMany_Returns400()
    {
        var ids = string.Join(",", Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()));
        var response = await _client.GetAsync($"/api/users?ids={ids}", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
