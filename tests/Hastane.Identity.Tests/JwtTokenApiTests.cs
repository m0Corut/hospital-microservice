using System.Net;
using System.Net.Http.Json;
using Hastane.Identity.Api.Dtos;
using Hastane.Shared.Security;
using Hastane.TestSupport;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Hastane.Identity.Tests;

// Token'ın ÜRETİMİ (login) ve DOĞRULANMASI (auth/me gibi korunan uçlar) birlikte.
public class JwtTokenApiTests : IClassFixture<IdentityApiFactory>
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _anonymous;

    public JwtTokenApiTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _anonymous = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<LoginResultDto> LoginAsync(string email, string password = IdentityTestData.GoodPassword)
    {
        var response = await _anonymous.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LoginResultDto>(Ct))!;
    }

    private async Task<HttpResponseMessage> MeAsync(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.WithBearer(token);
        return await client.GetAsync("/api/auth/me", Ct);
    }

    [Fact]
    public async Task EndToEnd_Register_Login_Me_WorksWithIdentitysOwnToken()
    {
        var email = IdentityTestData.NewEmail();
        var register = await _anonymous.PostAsJsonAsync("/api/auth/register",
            new { firstName = "Ayşe", lastName = "Yılmaz", email, password = IdentityTestData.GoodPassword }, Ct);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var registered = (await register.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!;

        var login = await LoginAsync(email);

        // Identity'nin ÖZEL anahtarla imzaladığı token, AÇIK anahtarla doğrulanıp korunan uca girebiliyor.
        var me = await MeAsync(login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meBody = (await me.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!;
        Assert.Equal(registered.Id, meBody.Id);
        Assert.Equal(email, meBody.Email);
        Assert.Equal(["Patient"], meBody.Roles);
    }

    [Fact]
    public async Task Login_ResponseShape_AndTokenContents()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var before = DateTime.UtcNow;

        var login = await LoginAsync(user.Email!);

        Assert.Equal("Bearer", login.TokenType);
        Assert.Equal(user.Id, login.User.Id);
        Assert.InRange(login.ExpiresAt, before.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));

        var jwt = new JsonWebToken(login.AccessToken);
        Assert.Equal("RS256", jwt.Alg);
        Assert.Equal(TestKeys.KeyId, jwt.Kid);
        Assert.Equal("hastane-identity", jwt.Issuer);
        Assert.Contains("hastane-api", jwt.Audiences);
        Assert.Equal(user.Id, jwt.Subject);
        Assert.Equal(user.Email, jwt.GetClaim(HastaneClaims.Email).Value);
        Assert.Equal("Test Kullanıcı", jwt.GetClaim(HastaneClaims.Name).Value);
        Assert.Equal([HastaneRoles.Patient], jwt.Claims.Where(c => c.Type == HastaneClaims.Role).Select(c => c.Value));
        Assert.False(string.IsNullOrEmpty(jwt.Id));
        // 30 dakika = 1800 sn; iat/nbf/exp üçü de var.
        Assert.Equal(TimeSpan.FromMinutes(30), jwt.ValidTo - jwt.ValidFrom);
        Assert.NotEqual(default, jwt.IssuedAt);
        // Hasta için doctor_id claim'i hiç yok.
        Assert.DoesNotContain(jwt.Claims, c => c.Type == HastaneClaims.DoctorId);
    }

    [Fact]
    public async Task Login_EachCallGetsDifferentJti()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var a = new JsonWebToken((await LoginAsync(user.Email!)).AccessToken);
        var b = new JsonWebToken((await LoginAsync(user.Email!)).AccessToken);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public async Task Login_Token_HasMultipleRoleClaims_AndDoctorIdOnlyWhenLinkedDoctor()
    {
        var linked = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor, HastaneRoles.Patient], "doc-123");
        var unlinked = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Doctor]);
        var notDoctorButHasId = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient], "doc-999");

        var linkedJwt = new JsonWebToken((await LoginAsync(linked.Email!)).AccessToken);
        Assert.Equal([HastaneRoles.Doctor, HastaneRoles.Patient],
            linkedJwt.Claims.Where(c => c.Type == HastaneClaims.Role).Select(c => c.Value).Order());
        Assert.Equal("doc-123", linkedJwt.GetClaim(HastaneClaims.DoctorId).Value);

        var unlinkedJwt = new JsonWebToken((await LoginAsync(unlinked.Email!)).AccessToken);
        Assert.DoesNotContain(unlinkedJwt.Claims, c => c.Type == HastaneClaims.DoctorId);

        // Doctor rolü yoksa bağ kaydı olsa bile claim'e girmez.
        var strayJwt = new JsonWebToken((await LoginAsync(notDoctorButHasId.Email!)).AccessToken);
        Assert.DoesNotContain(strayJwt.Claims, c => c.Type == HastaneClaims.DoctorId);
    }

    [Fact]
    public async Task Me_WithTestJwt_ReturnsUser_AndWithoutToken_Returns401WithProblemDetails()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);

        var ok = await MeAsync(TestJwt.Create(user.Id, [HastaneRoles.Patient]));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var none = await MeAsync(null);
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal("Bearer", none.Headers.WwwAuthenticate.ToString());
        Assert.Equal("application/problem+json", none.Content.Headers.ContentType!.MediaType);
        var body = await none.Content.ReadAsStringAsync(Ct);
        Assert.Contains("\"status\":401", body);
        Assert.Contains("traceId", body);
    }

    [Fact]
    public async Task Me_ForTokenOfDeletedUser_Returns404()
    {
        var response = await MeAsync(TestJwt.Create(Guid.NewGuid().ToString(), [HastaneRoles.Patient]));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Me_BrokenSignature_Returns401()
    {
        var response = await MeAsync(TestJwt.CreateWithBrokenSignature(Guid.NewGuid().ToString(), [HastaneRoles.Admin]));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Me_ExpiredToken_Returns401()
    {
        var response = await MeAsync(TestJwt.CreateExpired(Guid.NewGuid().ToString(), [HastaneRoles.Patient]));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ClockSkewIs30Seconds_NotFiveMinutes()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);

        // 10 sn önce dolmuş: 30 sn toleransın içinde, hâlâ geçer.
        var justExpired = TestJwt.Create(user.Id, [HastaneRoles.Patient], expires: TimeSpan.FromSeconds(-10));
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(justExpired)).StatusCode);

        // 2 dk önce dolmuş: varsayılan 5 dk tolerans olsaydı geçerdi; bizde 401.
        var twoMinutesAgo = TestJwt.Create(user.Id, [HastaneRoles.Patient], expires: TimeSpan.FromMinutes(-2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(twoMinutesAgo)).StatusCode);
    }

    [Fact]
    public async Task Me_ShortLivedToken_IsAcceptedWhileValid()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var token = TestJwt.Create(user.Id, [HastaneRoles.Patient], expires: TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.OK, (await MeAsync(token)).StatusCode);
    }

    [Theory]
    [InlineData("baska-issuer", "hastane-api")]
    [InlineData("hastane-identity", "baska-audience")]
    public async Task Me_WrongIssuerOrAudience_Returns401(string issuer, string audience)
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var token = TestJwt.Create(user.Id, [HastaneRoles.Patient], issuer: issuer, audience: audience);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Me_AlgNoneToken_Returns401()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Admin]);
        var response = await MeAsync(TestJwt.CreateAlgNone(user.Id, [HastaneRoles.Admin]));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_Hs256TokenSignedWithPublicKeyAsSecret_AlgConfusion_Returns401()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Admin]);
        var forged = TestJwt.CreateHs256WithPublicKeyAsSecret(user.Id, [HastaneRoles.Admin]);

        // Aynı claim'ler, doğru issuer/audience, "imza" açık anahtardan türetilmiş: yine de reddedilmeli.
        var response = await MeAsync(forged);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Aynı sahte token Admin uçlarında da işe yaramaz.
        var roles = await _factory.CreateClient().WithBearer(forged).GetAsync("/api/roles", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, roles.StatusCode);
    }

    [Fact]
    public async Task Me_TokenSignedByUnknownKey_Returns401()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        using var rogue = System.Security.Cryptography.RSA.Create(2048);
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Subject = new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", user.Id)]),
            Issuer = "hastane-identity",
            Audience = "hastane-api",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.RsaSecurityKey(rogue), "RS256"),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Register_AndLogin_AreAnonymous_ButUsersAndRolesNeedToken()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync($"/api/users/{Guid.NewGuid()}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _anonymous.GetAsync("/api/roles", Ct)).StatusCode);
        // Giriş uçları token olmadan çalışır (400/401 gelir ama "kimliksiz" diye reddedilmez).
        var login = await _anonymous.PostAsJsonAsync("/api/auth/login", new { email = "yok@example.com", password = "Yanlis123" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("Giriş başarısız", await login.Content.ReadAsStringAsync(Ct));
    }
}
