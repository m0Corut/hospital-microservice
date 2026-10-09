using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Infrastructure.Security;
using Hastane.WebUI.ViewModels.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

public class AccountController(IAuthClient auth, IAccessTokenValidator tokenValidator, ILogger<AccountController> logger)
    : AppController
{
    private const string GenericLoginError = "E-posta veya şifre hatalı.";

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToLocal(returnUrl);
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    // Antiforgery global (AutoValidateAntiforgeryToken); POST'ta belirteç yoksa istek 400 ile reddedilir.
    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await auth.LoginAsync(model.Email, model.Password, ct);
        if (!result.TryGetValue(out var login))
        {
            // Hata metni bilerek genel: hesabın var olup olmadığı sızmaz (Identity de aynı şeyi yapar).
            ModelState.AddModelError(string.Empty, result.Kind == ApiResultKind.Locked
                ? "Hesap geçici olarak kilitlendi. Lütfen daha sonra tekrar deneyin."
                : GenericLoginError);
            return View(model);
        }

        // Gelen token'ı kendi anahtarımızla doğrula; ancak ondan sonra principal kur.
        var validated = await tokenValidator.ValidateAsync(login.AccessToken);
        if (validated is null)
        {
            logger.LogError("Identity geçersiz bir token döndürdü; giriş reddedildi.");
            ModelState.AddModelError(string.Empty, "Giriş şu anda tamamlanamıyor. Lütfen daha sonra tekrar deneyin.");
            return View(model);
        }

        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = validated.ExpiresAt,   // çerez ömrü = token ömrü; kayan süre yok
            AllowRefresh = false,
        };
        properties.StoreTokens([new AuthenticationToken { Name = AuthConstants.AccessTokenName, Value = login.AccessToken }]);

        await HttpContext.SignInAsync(AuthConstants.CookieScheme, validated.Principal, properties);
        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await auth.RegisterAsync(model.FirstName, model.LastName, model.Email, model.Password, ct);
        if (!result.IsSuccess)
        {
            ModelState.AddApiProblem(result, "Kayıt tamamlanamadı.");
            return View(model);
        }

        SetSuccess("Kayıt tamamlandı. Şimdi giriş yapabilirsiniz.");
        return RedirectToAction(nameof(Login));
    }

    // Çıkış POST: bir bağlantıyla (GET) çıkış yaptırılamasın (başka sitedeki <img src=/Account/Logout> hilesi).
    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthConstants.CookieScheme);
        return RedirectToAction("Index", "Home", new { area = "" });
    }
}
