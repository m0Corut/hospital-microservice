using System.Net;
using System.Net.Http.Json;
using Hastane.Branch.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;

namespace Hastane.Branch.Tests;

// JWT-TASARIM.md §5/§7: Branch okuması anonim, yazması yalnız Admin.
public class BranchAuthorizationTests : IClassFixture<BranchApiFactory>
{
    private const string SomeId = "64b7f0f0f0f0f0f0f0f0f0f0";

    private readonly BranchApiFactory _factory;

    public BranchAuthorizationTests(BranchApiFactory factory) => _factory = factory;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientWith(string? token)
    {
        var client = _factory.CreateClient();
        return token is null ? client : client.WithBearer(token);
    }

    private static string AdminToken => TestJwt.Create("admin-1", [HastaneRoles.Admin]);

    private async Task<string> CreateBranchAsAdminAsync(string name)
    {
        var response = await ClientWith(AdminToken).PostAsJsonAsync("/api/branches", new CreateBranchDto { Name = name }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ResultBranchDto>(Ct))!.Id;
    }

    // Üç yazma ucu: POST, PUT, DELETE (hepsi Admin).
    private static async Task<HttpResponseMessage> CallWriteAsync(HttpClient client, string verb, string id) => verb switch
    {
        "POST" => await client.PostAsJsonAsync("/api/branches", new CreateBranchDto { Name = "Yeni" }, Ct),
        "PUT" => await client.PutAsJsonAsync($"/api/branches/{id}", new UpdateBranchDto { Id = id, Name = "Güncel" }, Ct),
        "DELETE" => await client.DeleteAsync($"/api/branches/{id}", Ct),
        _ => throw new ArgumentOutOfRangeException(nameof(verb)),
    };

    [Fact]
    public void EveryControllerEndpoint_DeclaresAuthorizationOrAnonymous()
    {
        var missing = AuthorizationCoverage.FindEndpointsWithoutAuthorizationMetadata(_factory.Services);

        Assert.True(missing.Count == 0, "Yetki bilgisi olmayan uçlar: " + string.Join("; ", missing));
    }

    [Fact]
    public async Task AnonymousReads_ListAndGetById_Return200WithoutToken()
    {
        var id = await CreateBranchAsAdminAsync("Anonim Okuma");
        var anonymous = ClientWith(null);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/branches", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/branches/{id}", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Writes_WithoutToken_Return401(string verb)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await CallWriteAsync(ClientWith(null), verb, SomeId)).StatusCode);

    public static TheoryData<string, string> BadTokens()
    {
        var data = new TheoryData<string, string>();
        foreach (var verb in new[] { "POST", "PUT", "DELETE" })
        {
            data.Add(verb, "expired");
            data.Add(verb, "broken-signature");
            data.Add(verb, "alg-none");
            data.Add(verb, "hs256-public-key-as-secret");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task Writes_WithInvalidToken_Return401(string verb, string kind)
    {
        var roles = new[] { HastaneRoles.Admin }; // Admin iddiası bile sahte/süresi dolmuş token'ı kurtarmaz
        var token = kind switch
        {
            "expired" => TestJwt.CreateExpired("admin-1", roles),
            "broken-signature" => TestJwt.CreateWithBrokenSignature("admin-1", roles),
            "alg-none" => TestJwt.CreateAlgNone("admin-1", roles),
            _ => TestJwt.CreateHs256WithPublicKeyAsSecret("admin-1", roles),
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await CallWriteAsync(ClientWith(token), verb, SomeId)).StatusCode);
    }

    [Theory]
    [InlineData("POST", HastaneRoles.Patient)]
    [InlineData("PUT", HastaneRoles.Patient)]
    [InlineData("DELETE", HastaneRoles.Patient)]
    [InlineData("POST", HastaneRoles.Doctor)]
    [InlineData("PUT", HastaneRoles.Doctor)]
    [InlineData("DELETE", HastaneRoles.Doctor)]
    public async Task Writes_WithWrongRole_Return403(string verb, string role)
    {
        var client = ClientWith(TestJwt.Create("user-1", [role], doctorId: role == HastaneRoles.Doctor ? "d1" : null));

        Assert.Equal(HttpStatusCode.Forbidden, (await CallWriteAsync(client, verb, SomeId)).StatusCode);
    }

    [Fact]
    public async Task Writes_WithAdminToken_Succeed()
    {
        var admin = ClientWith(AdminToken);
        var id = await CreateBranchAsAdminAsync("Admin Yazar");

        Assert.Equal(HttpStatusCode.NoContent, (await CallWriteAsync(admin, "PUT", id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await CallWriteAsync(admin, "DELETE", id)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CallWriteAsync(admin, "POST", SomeId)).StatusCode);
    }

    [Fact]
    public async Task Forbidden_And_Unauthorized_ResponsesAreProblemJson()
    {
        var unauthorized = await CallWriteAsync(ClientWith(null), "POST", SomeId);
        Assert.Equal("application/problem+json", unauthorized.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Bearer", unauthorized.Headers.WwwAuthenticate.ToString());

        var forbidden = await CallWriteAsync(ClientWith(TestJwt.Create("u", [HastaneRoles.Patient])), "POST", SomeId);
        Assert.Equal("application/problem+json", forbidden.Content.Headers.ContentType?.MediaType);
    }
}
