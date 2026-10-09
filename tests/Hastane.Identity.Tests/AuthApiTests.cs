using System.Net;
using System.Net.Http.Json;
using Hastane.Identity.Api.Dtos;

namespace Hastane.Identity.Tests;

public class AuthApiTests : IClassFixture<IdentityApiFactory>
{
    private const string GoodPassword = "Passw0rd1";
    private readonly HttpClient _client;

    public AuthApiTests(IdentityApiFactory factory) => _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static object RegisterBody(string email, string password = GoodPassword)
        => new { firstName = "Ayşe", lastName = "Yılmaz", email, password };

    // Hiçbir cevapta bu alanlar geçmemeli (gizli veri sızıntısı kontrolü).
    private static void AssertNoSecrets(string json)
    {
        foreach (var forbidden in new[] { "passwordhash", "securitystamp", "phonenumber", "lockout", "concurrencystamp" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<UserSummaryDto> RegisterAsync(string email, string password = GoodPassword)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", RegisterBody(email, password), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertNoSecrets(body);
        return (await response.Content.ReadFromJsonAsync<UserSummaryDto>(Ct))!;
    }

    [Fact]
    public async Task Register_ThenLogin_Works()
    {
        var email = NewEmail();
        var registered = await RegisterAsync(email);
        Assert.Equal(email, registered.Email);
        Assert.Equal("Ayşe", registered.FirstName);
        Assert.Equal(["Patient"], registered.Roles);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = GoodPassword }, Ct);
        var body = await login.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        AssertNoSecrets(body);

        var result = await login.Content.ReadFromJsonAsync<LoginResultDto>(Ct);
        Assert.Equal(registered.Id, result!.Id);
        Assert.Equal(["Patient"], result.Roles);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task Register_ReturnsLocationOfUser()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail()), Ct);
        var user = await response.Content.ReadFromJsonAsync<UserSummaryDto>(Ct);
        Assert.EndsWith($"/api/users/{user!.Id}", response.Headers.Location!.AbsolutePath);

        var fetched = await _client.GetAsync(response.Headers.Location, Ct);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task Register_IgnoresRoleInBody_MassAssignment()
    {
        var email = NewEmail();
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new
            {
                firstName = "Kötü",
                lastName = "Niyetli",
                email,
                password = GoodPassword,
                role = "Admin",
                roles = new[] { "Admin" },
                id = "00000000-0000-0000-0000-000000000001"
            }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserSummaryDto>(Ct);
        Assert.Equal(["Patient"], user!.Roles);
        Assert.NotEqual("00000000-0000-0000-0000-000000000001", user.Id);

        // Veritabanındaki gerçek durum da aynı: GET ile tekrar oku.
        var fetched = await _client.GetFromJsonAsync<UserSummaryDto>($"/api/users/{user.Id}", Ct);
        Assert.Equal(["Patient"], fetched!.Roles);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409_EvenWithDifferentCase()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var again = await _client.PostAsJsonAsync("/api/auth/register", RegisterBody(email.ToUpperInvariant()), Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData("Ab1")]            // çok kısa
    [InlineData("passw0rdd")]      // büyük harf yok
    [InlineData("PASSW0RDD")]      // küçük harf yok
    [InlineData("Passwordd")]      // rakam yok
    public async Task Register_WeakPassword_Returns400WithIdentityErrors(string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", RegisterBody(NewEmail(), password), Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"errors\"", body);
        Assert.Contains("Password", body);
    }

    [Fact]
    public async Task Register_InvalidEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", RegisterBody("bu-bir-eposta-degil"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_And_UnknownEmail_ReturnIdentical401()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        var wrongPassword = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Yanlis123" }, Ct);
        var unknownEmail = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = NewEmail(), password = "Yanlis123" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        // Gövde harfi harfine aynı: "bu e-posta kayıtlı mı" sorusunun cevabı sızmaz.
        Assert.Equal(await unknownEmail.Content.ReadAsStringAsync(Ct), await wrongPassword.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Login_FiveWrongAttempts_LocksAccount_EvenForCorrectPassword()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        for (var i = 0; i < 5; i++)
            await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Yanlis123" }, Ct);

        var locked = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = GoodPassword }, Ct);
        var body = await locked.Content.ReadAsStringAsync(Ct);

        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
        AssertNoSecrets(body);
    }

    [Fact]
    public async Task Login_FourWrongAttempts_DoNotLock_AndSuccessResetsCounter()
    {
        var email = NewEmail();
        await RegisterAsync(email);

        for (var i = 0; i < 4; i++)
        {
            var wrong = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Yanlis123" }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var ok = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = GoodPassword }, Ct);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // Sayaç sıfırlandı: dört yanlış daha hâlâ kilitlemez.
        for (var i = 0; i < 4; i++)
        {
            var wrong = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Yanlis123" }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }
    }

    [Fact]
    public async Task Login_MissingFields_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "", password = "" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
