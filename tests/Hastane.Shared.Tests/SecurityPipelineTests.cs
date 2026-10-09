using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Shared.Tests;

// Hastane.Shared'ın güvenlik hattı, veritabanı ve Identity olmadan: küçük bir test sunucusuyla.
public sealed class SecurityPipelineTests : IAsyncLifetime
{
    private WebApplication _app = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // useTestKey=false: üretim (gömülü) açık anahtarıyla çalışan sunucu.
    private static async Task<WebApplication> StartAsync(bool useTestKey, Dictionary<string, string?>? extraConfig = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (extraConfig is not null) builder.Configuration.AddInMemoryCollection(extraConfig);
        builder.Services.AddHastaneJwtAuthentication(builder.Configuration);
        builder.Services.AddHastaneCurrentUser();
        if (useTestKey) builder.WebHost.UseTestJwt();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/open", () => "ok").AllowAnonymous();
        app.MapGet("/authed", () => "ok").RequireAuthorization(HastanePolicies.AuthenticatedUser);
        app.MapGet("/admin", () => "ok").RequireAuthorization(HastanePolicies.AdminOnly);
        app.MapGet("/doctor", () => "ok").RequireAuthorization(HastanePolicies.DoctorOnly);
        app.MapGet("/patient", () => "ok").RequireAuthorization(HastanePolicies.PatientOnly);
        app.MapGet("/doctor-or-admin", () => "ok").RequireAuthorization(HastanePolicies.DoctorOrAdmin);
        app.MapGet("/who", (ICurrentUser user) => Results.Json(new
        {
            user.IsAuthenticated,
            user.UserId,
            user.Email,
            user.DoctorId,
            user.Roles,
            user.IsAdmin,
            IsDoctor = user.IsInRole(HastaneRoles.Doctor),
        })).RequireAuthorization();
        await app.StartAsync(Ct);
        return app;
    }

    public async ValueTask InitializeAsync() => _app = await StartAsync(useTestKey: true);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private HttpClient Client(string? token = null)
    {
        var client = _app.GetTestClient();
        return token is null ? client : client.WithBearer(token);
    }

    [Fact]
    public async Task AnonymousEndpoint_NeedsNoToken()
        => Assert.Equal(HttpStatusCode.OK, (await Client().GetAsync("/open", Ct)).StatusCode);

    [Fact]
    public async Task NoToken_Returns401_WithBearerChallengeAndProblemDetails()
    {
        var response = await Client().GetAsync("/authed", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(401, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(json.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task InvalidToken_Returns401_WithInvalidTokenChallenge()
    {
        var response = await Client(TestJwt.CreateExpired("u1", [HastaneRoles.Admin])).GetAsync("/admin", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task WrongRole_Returns403_WithProblemDetails_NotRedirect()
    {
        var response = await Client(TestJwt.Create("u1", [HastaneRoles.Patient])).GetAsync("/admin", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(403, json.RootElement.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("/admin", HastaneRoles.Admin, true)]
    [InlineData("/admin", HastaneRoles.Doctor, false)]
    [InlineData("/doctor", HastaneRoles.Doctor, true)]
    [InlineData("/doctor", HastaneRoles.Patient, false)]
    [InlineData("/patient", HastaneRoles.Patient, true)]
    [InlineData("/patient", HastaneRoles.Admin, false)]
    [InlineData("/doctor-or-admin", HastaneRoles.Doctor, true)]
    [InlineData("/doctor-or-admin", HastaneRoles.Admin, true)]
    [InlineData("/doctor-or-admin", HastaneRoles.Patient, false)]
    [InlineData("/authed", HastaneRoles.Patient, true)]
    public async Task Policies_AllowOnlyIntendedRoles(string path, string role, bool allowed)
    {
        var response = await Client(TestJwt.Create("u1", [role])).GetAsync(path, Ct);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserWithMultipleRoles_PassesPolicyOfAnyOfThem()
    {
        var token = TestJwt.Create("u1", [HastaneRoles.Patient, HastaneRoles.Doctor]);
        Assert.Equal(HttpStatusCode.OK, (await Client(token).GetAsync("/doctor", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client(token).GetAsync("/patient", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(token).GetAsync("/admin", Ct)).StatusCode);
    }

    [Fact]
    public async Task CurrentUser_ReadsShortClaimNames_BecauseInboundMappingIsOff()
    {
        var token = TestJwt.Create("user-42", [HastaneRoles.Doctor, HastaneRoles.Patient], doctorId: "doc-7",
            email: "dr@example.com");

        var who = await Client(token).GetFromJsonAsync<JsonElement>("/who", Ct);

        Assert.True(who.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("user-42", who.GetProperty("userId").GetString());
        Assert.Equal("dr@example.com", who.GetProperty("email").GetString());
        Assert.Equal("doc-7", who.GetProperty("doctorId").GetString());
        Assert.False(who.GetProperty("isAdmin").GetBoolean());
        Assert.True(who.GetProperty("isDoctor").GetBoolean());
        Assert.Equal(["Doctor", "Patient"], who.GetProperty("roles").EnumerateArray().Select(e => e.GetString()!).Order());
    }

    [Fact]
    public async Task CurrentUser_WithoutDoctorId_HasNullDoctorId()
    {
        var who = await Client(TestJwt.Create("u1", [HastaneRoles.Admin])).GetFromJsonAsync<JsonElement>("/who", Ct);
        Assert.Equal(JsonValueKind.Null, who.GetProperty("doctorId").ValueKind);
        Assert.True(who.GetProperty("isAdmin").GetBoolean());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("hs256")]
    [InlineData("tampered")]
    public async Task ForgedTokens_AreRejected(string kind)
    {
        var token = kind switch
        {
            "none" => TestJwt.CreateAlgNone("u1", [HastaneRoles.Admin]),
            "hs256" => TestJwt.CreateHs256WithPublicKeyAsSecret("u1", [HastaneRoles.Admin]),
            _ => TestJwt.CreateWithBrokenSignature("u1", [HastaneRoles.Admin]),
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(token).GetAsync("/admin", Ct)).StatusCode);
    }

    [Fact]
    public async Task Hs256Forgery_WithTheEmbeddedProductionPublicKeyAsSecret_IsRejected()
    {
        await using var prod = await StartAsync(useTestKey: false);
        var forged = TestJwt.CreateHs256WithPublicKeyAsSecret("u1", [HastaneRoles.Admin], JwtKeys.LoadEmbeddedPublicKeyPem());
        Assert.Equal(HttpStatusCode.Unauthorized, (await prod.GetTestClient().WithBearer(forged).GetAsync("/admin", Ct)).StatusCode);
    }

    [Fact]
    public async Task ProductionKey_RejectsTestTokens_SoTestsNeverTouchTheRealKey()
    {
        await using var prod = await StartAsync(useTestKey: false);
        var response = await prod.GetTestClient().WithBearer(TestJwt.Create("u1", [HastaneRoles.Admin])).GetAsync("/admin", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PublicKeyPemConfiguration_OverridesEmbeddedKey()
    {
        await using var app = await StartAsync(useTestKey: false,
            new Dictionary<string, string?> { ["Jwt:PublicKeyPem"] = TestKeys.PublicKeyPem });
        var response = await app.GetTestClient().WithBearer(TestJwt.Create("u1", [HastaneRoles.Admin])).GetAsync("/admin", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task IssuerAndAudience_AreConfigurable()
    {
        await using var app = await StartAsync(useTestKey: true,
            new Dictionary<string, string?> { ["Jwt:Issuer"] = "baska", ["Jwt:Audience"] = "baska-api" });
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.WithBearer(TestJwt.Create("u1", [HastaneRoles.Admin])).GetAsync("/admin", Ct)).StatusCode);

        var matching = TestJwt.Create("u1", [HastaneRoles.Admin], issuer: "baska", audience: "baska-api");
        var ok = await app.GetTestClient().WithBearer(matching).GetAsync("/admin", Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public void EmbeddedPublicKey_IsAValid2048BitRsaKey()
    {
        var pem = JwtKeys.LoadEmbeddedPublicKeyPem();
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        Assert.Equal(2048, rsa.KeySize);
        Assert.DoesNotContain("PRIVATE", pem);
    }

    [Fact]
    public void KeyId_IsSameForPrivateAndPublicHalfOfSamePair_AndDiffersBetweenPairs()
    {
        var priv = JwtKeys.CreatePrivateKey(TestKeys.PrivateKeyPem);
        var pub = JwtKeys.CreatePublicKey(TestKeys.PublicKeyPem);
        Assert.Equal(priv.KeyId, pub.KeyId);
        Assert.Equal(TestKeys.KeyId, pub.KeyId);

        var embedded = JwtKeys.CreatePublicKey(JwtKeys.LoadEmbeddedPublicKeyPem());
        Assert.NotEqual(TestKeys.KeyId, embedded.KeyId);
    }
}
