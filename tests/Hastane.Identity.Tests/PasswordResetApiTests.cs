using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Hastane.Identity.Api.Entities;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Hastane.Identity.Tests;

public class PasswordResetApiTests : IClassFixture<IdentityApiFactory>
{
    private const string NewPassword = "Yeni1Parola9";
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _client;

    public PasswordResetApiTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> ForgotAsync(string email)
        => _client.PostAsJsonAsync("/api/auth/forgot-password", new { email }, Ct);

    private Task<HttpResponseMessage> ResetAsync(string email, string token, string password = NewPassword)
        => _client.PostAsJsonAsync("/api/auth/reset-password", new { email, token, newPassword = password }, Ct);

    private Task<HttpResponseMessage> LoginAsync(string email, string password)
        => _client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);

    // Doğrudan Identity'den token: e-posta akışı dışındaki testler için hızlı yol.
    private async Task<string> CreateTokenAsync(AppUser user)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await userManager.GeneratePasswordResetTokenAsync((await userManager.FindByIdAsync(user.Id))!);
    }

    // ---- forgot-password ----

    [Fact]
    public async Task Forgot_ExistingAndUnknownUser_ReturnSame202AndBody()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);

        var existing = await ForgotAsync(user.Email!);
        var unknown = await ForgotAsync(IdentityTestData.NewEmail());

        Assert.Equal(HttpStatusCode.Accepted, existing.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(await existing.Content.ReadAsStringAsync(Ct), await unknown.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Forgot_SendsEmailOnlyForExistingUser_WithConfiguredBaseUrl_EvenIfHostHeaderIsForged()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var unknownEmail = IdentityTestData.NewEmail();

        // Host başlığı zehirleme denemesi: bağlantı yine WebUi:BaseUrl ayarıyla başlamalı.
        foreach (var email in new[] { unknownEmail, user.Email! })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
            {
                Content = JsonContent.Create(new { email }),
            };
            request.Headers.Host = "evil.example.com";
            request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "evil.example.com");
            Assert.Equal(HttpStatusCode.Accepted, (await _client.SendAsync(request, Ct)).StatusCode);
        }

        var sent = await _factory.Notification.WaitForEmailAsync(user.Email!);
        Assert.Empty(_factory.Notification.EmailsTo(unknownEmail));
        Assert.Equal("POST /api/internal/notifications/email", sent.Path);
        Assert.Equal(IdentityApiFactory.InternalApiKey, sent.InternalKey);
        Assert.StartsWith($"{IdentityApiFactory.WebUiBaseUrl}/Account/ResetPassword?email=", sent.TextBody.Split('\n').First(l => l.StartsWith("http")));
        Assert.Contains($"email={Uri.EscapeDataString(user.Email!)}", sent.TextBody);
        Assert.DoesNotContain("evil.example.com", sent.TextBody);
        Assert.DoesNotContain("evil.example.com", sent.HtmlBody);
    }

    [Fact]
    public async Task Forgot_TokenFromEmail_ResetsPassword()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        await ForgotAsync(user.Email!);

        var sent = await _factory.Notification.WaitForEmailAsync(user.Email!);
        var token = Uri.UnescapeDataString(Regex.Match(sent.TextBody, @"[?&]token=([^\s&]+)").Groups[1].Value);

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(user.Email!, token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email!, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Forgot_NotificationDown_StillReturns202()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        _factory.Notification.Fail = true;
        try
        {
            var response = await ForgotAsync(user.Email!);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        finally
        {
            _factory.Notification.Fail = false;
        }
    }

    [Fact]
    public async Task Forgot_MoreThanThreeInWindow_StillReturns202_ButOnlyThreeEmailsAreSent()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync(user.Email!)).StatusCode);

        await _factory.Notification.WaitForEmailAsync(user.Email!, count: 3);
        await Task.Delay(500, Ct); // fazladan e-posta gelseydi şimdiye kuyruktan çıkmış olurdu
        Assert.Equal(3, _factory.Notification.EmailsTo(user.Email!).Count);
    }

    [Fact]
    public async Task Forgot_InvalidEmailFormat_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await ForgotAsync("bu-bir-eposta-degil")).StatusCode);
    }

    // ---- reset-password ----

    [Fact]
    public async Task Reset_ValidToken_Returns204_NewPasswordWorks_OldDoesNot()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var token = await CreateTokenAsync(user);

        var response = await ResetAsync(user.Email!, token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email!, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(user.Email!, IdentityTestData.GoodPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_ChangesSecurityStamp()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var before = (await IdentityTestData.GetUserAsync(_factory, user.Id)).SecurityStamp;
        var token = await CreateTokenAsync(user);

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(user.Email!, token)).StatusCode);

        Assert.NotEqual(before, (await IdentityTestData.GetUserAsync(_factory, user.Id)).SecurityStamp);
    }

    [Fact]
    public async Task Reset_InvalidToken_And_UnknownUser_ReturnIdentical400()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var validTokenOfOther = await CreateTokenAsync(user);

        var badToken = await ResetAsync(user.Email!, "bozuk-token");
        var unknownUser = await ResetAsync(IdentityTestData.NewEmail(), validTokenOfOther);

        Assert.Equal(HttpStatusCode.BadRequest, badToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownUser.StatusCode);
        Assert.Equal(await badToken.Content.ReadAsStringAsync(Ct), await unknownUser.Content.ReadAsStringAsync(Ct));
        // Şifre değişmedi.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email!, IdentityTestData.GoodPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_TokenOfAnotherUser_Returns400()
    {
        var alice = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var bob = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var aliceToken = await CreateTokenAsync(alice);

        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(bob.Email!, aliceToken)).StatusCode);
    }

    [Fact]
    public async Task Reset_ReusedToken_Returns400()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var token = await CreateTokenAsync(user);

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(user.Email!, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ResetAsync(user.Email!, token, "Baska1Parola8")).StatusCode);
        // İkinci deneme şifreyi değiştirmedi.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email!, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_WeakPassword_Returns400WithFieldErrors()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        var token = await CreateTokenAsync(user);

        var response = await ResetAsync(user.Email!, token, "kisa");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email!, IdentityTestData.GoodPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_LockedAccount_IsUnlockedAndFailureCounterReset()
    {
        var user = await IdentityTestData.CreateUserAsync(_factory, [HastaneRoles.Patient]);
        for (var i = 0; i < 5; i++)
            await LoginAsync(user.Email!, "Yanlis1Parola");
        Assert.Equal(HttpStatusCode.Locked, (await LoginAsync(user.Email!, IdentityTestData.GoodPassword)).StatusCode);

        var token = await CreateTokenAsync(user);
        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(user.Email!, token)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(user.Email!, NewPassword)).StatusCode);
        var after = await IdentityTestData.GetUserAsync(_factory, user.Id);
        Assert.Equal(0, after.AccessFailedCount);
        Assert.Null(after.LockoutEnd);
    }

    [Fact]
    public async Task Reset_MissingFields_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/reset-password", new { email = "a@b.co" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
