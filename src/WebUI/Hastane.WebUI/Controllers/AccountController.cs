using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Infrastructure.Security;
using Hastane.WebUI.ViewModels.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hastane.WebUI.Controllers;

public class AccountController(
    IAuthClient auth, IAccountSettingsClient settings, IAccessTokenValidator tokenValidator, ILogger<AccountController> logger,
    IAccountRecoveryClient recovery, IAccountPhotoClient photos, IBranchClient branches)
    : AppController
{
    private const string GenericLoginError = "E-posta veya şifre hatalı.";
    private const string ForgotPasswordMessage = "Bu e-posta kayıtlıysa sıfırlama bağlantısı gönderildi.";
    private const string InvalidResetLinkMessage = "Bağlantı geçersiz ya da süresi dolmuş. Lütfen yeni bir sıfırlama bağlantısı isteyin.";
    private const string PhotoTooLargeMessage = "Fotoğraf en çok 2 MB olabilir.";
    private const string PhotoTypeMessage = "Yalnız PNG, JPEG ya da WebP görseller kabul edilir.";

    // Sunucu tarafı fotoğraf sınırı: 2 MB dosya + multipart zarf payı. Kimlik servisi de aynı sınırı uygular.
    private const int MaxPhotoBytes = 2 * 1024 * 1024;
    private const int MaxPhotoRequestBytes = MaxPhotoBytes + 64 * 1024;
    private static readonly string[] PhotoTypes = ["image/png", "image/jpeg", "image/webp"];

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

    // Şifremi unuttum: kullanıcı var/yok, hız sınırı aşıldı/aşılmadı ayırt edilemesin diye (biçim hatası dışında) hep aynı mesaj.
    [HttpGet, AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await recovery.ForgotPasswordAsync(model.Email.Trim(), ct);
        if (result.Kind == ApiResultKind.ValidationFailed)
        {
            ModelState.AddApiProblem(result, "E-posta adresi geçersiz.");
            return View(model);
        }

        SetSuccess(ForgotPasswordMessage);
        return RedirectToAction(nameof(Login));
    }

    // Sıfırlama bağlantısı e-postadan gelir (?email=&token=). Belirteç adreste kalmasın diye form gizli alanla POST eder;
    // sayfa dış kaynak yüklemez ve Referer göndermez. Belirteç hiçbir yerde günlüğe yazılmaz.
    [HttpGet, AllowAnonymous, NoReferrer]
    public IActionResult ResetPassword(string? email, string? token)
        => View(new ResetPasswordViewModel { Email = email ?? string.Empty, Token = token ?? string.Empty });

    [HttpPost, AllowAnonymous, NoReferrer]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.Token) || string.IsNullOrWhiteSpace(model.Email)) return View(model);

        var result = await recovery.ResetPasswordAsync(model.Email.Trim(), model.Token, model.NewPassword, ct);
        if (result.IsSuccess)
        {
            SetSuccess("Şifreniz sıfırlandı. Yeni şifrenizle giriş yapabilirsiniz.");
            return RedirectToAction(nameof(Login));
        }

        // Zayıf şifre: alan hataları (hangi kural) gösterilir. Geçersiz/süresi dolmuş/kullanılmış belirteç: hep aynı genel metin.
        if (result.Kind == ApiResultKind.ValidationFailed && result.Problem is { Errors.Count: > 0 })
            ModelState.AddApiProblem(result);
        else
            ModelState.AddModelError(string.Empty, InvalidResetLinkMessage);

        return View(model);
    }

    // Doktor başvurusu (anonim): hesap + bekleyen başvuru birlikte oluşur; rol onaydan sonra Admin tarafından verilir.
    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> RegisterDoctor(CancellationToken ct)
        => View(await FillBranchesAsync(new RegisterDoctorViewModel(), ct));

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> RegisterDoctor(RegisterDoctorViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(await FillBranchesAsync(model, ct));

        var result = await recovery.RegisterDoctorAsync(model.FirstName.Trim(), model.LastName.Trim(), model.Email.Trim(), model.Password,
            model.BranchId, model.LicenseNumber.Trim(), string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim(), ct);
        if (!result.IsSuccess)
        {
            if (result.Kind == ApiResultKind.Conflict)
                ModelState.AddModelError(nameof(RegisterDoctorViewModel.Email), "Bu e-posta adresi zaten kayıtlı.");
            else
                ModelState.AddApiProblem(result, "Başvuru gönderilemedi.");
            return View(await FillBranchesAsync(model, ct));
        }

        SetSuccess("Başvurunuz alındı; onaylanınca doktor paneliniz açılır. Bu arada e-postanız ve şifrenizle giriş yapabilirsiniz.");
        return RedirectToAction(nameof(Login));
    }

    // Giriş yapmış kullanıcının kendi başvurusunun durumu (kimlik token'dan; başkasının başvurusu sorgulanamaz).
    [HttpGet, Authorize]
    public async Task<IActionResult> DoctorApplication(CancellationToken ct)
    {
        var result = await recovery.GetMyDoctorApplicationAsync(ct);
        if (result.Kind == ApiResultKind.NotFound) return View(new DoctorApplicationStatusViewModel());

        var application = result.Value ?? throw new InvalidOperationException("Başvuru yanıtı boş.");
        string? branchName = null;
        if (!string.IsNullOrEmpty(application.BranchId))
        {
            var list = await branches.GetAllAsync(ApiFailures.All, ct);
            branchName = list.Value?.FirstOrDefault(b => b.Id == application.BranchId)?.Name;
        }

        return View(new DoctorApplicationStatusViewModel { Application = application, BranchName = branchName });
    }

    private async Task<RegisterDoctorViewModel> FillBranchesAsync(RegisterDoctorViewModel model, CancellationToken ct)
    {
        var list = await branches.GetAllAsync(ApiFailures.All, ct);
        model.BranchesUnavailable = !list.IsSuccess || list.Value is not { Count: > 0 };
        model.Branches = (list.Value ?? []).Select(b => new SelectListItem(b.Name, b.Id, b.Id == model.BranchId)).ToList();
        return model;
    }

    // Hesap ayarları (tüm roller): ad/soyad düzenleme; e-posta ve roller salt okunur.
    [HttpGet, Authorize]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var me = (await settings.GetMeAsync(ct)).Value!;
        return View(new ProfileViewModel { FirstName = me.FirstName, LastName = me.LastName, Email = me.Email, Roles = me.Roles, PhotoUrl = PhotoUrlFor(me) });
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Profile(ProfileViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await settings.UpdateProfileAsync(model.FirstName.Trim(), model.LastName.Trim(), ct);
            if (result.IsSuccess)
            {
                // Menüdeki ad oturum belirtecinden (token) gelir; belirteç yenilenmediği için yeni ad bir sonraki girişte görünür.
                // Kullanıcıyı çıkış yaptırmak yerine açıkça bilgilendiririz: ad değişimi güvenlik olayı değil.
                SetSuccess("Bilgileriniz güncellendi. Menüdeki adınız bir sonraki girişinizde yenilenir.");
                return RedirectToAction(nameof(Profile));
            }

            ModelState.AddApiProblem(result, "Bilgiler güncellenemedi.");
        }

        // E-posta ve roller formdan alınmaz; her seferinde API'den okunur.
        var me = (await settings.GetMeAsync(ct)).Value!;
        model.Email = me.Email;
        model.Roles = me.Roles;
        model.PhotoUrl = PhotoUrlFor(me);
        return View(model);
    }

    // Dosya diske yazılmaz: form gövdesi bellekte tamponlanır (Program.cs: FormOptions.MemoryBufferThreshold) ve akış olarak iletilir.
    [HttpPost, Authorize]
    [RequestSizeLimit(MaxPhotoRequestBytes)]
    public async Task<IActionResult> UploadPhoto(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            SetWarning("Yüklemek için bir görsel seçin.");
        else if (file.Length > MaxPhotoBytes)
            SetWarning(PhotoTooLargeMessage);
        else if (!PhotoTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            SetWarning(PhotoTypeMessage);
        else
        {
            // Kullanıcının dosya adı iletilmez; tür başlığı yalnız izinli üç değerden biri. Asıl tür denetimi Identity'de (içerik imzası).
            await using var stream = file.OpenReadStream();
            var contentType = PhotoTypes.First(t => string.Equals(t, file.ContentType, StringComparison.OrdinalIgnoreCase));
            var result = await photos.UploadAsync(stream, contentType, ct);
            if (result.IsSuccess)
                SetSuccess("Fotoğrafınız güncellendi.");
            else if (result.StatusCode == StatusCodes.Status413PayloadTooLarge)
                SetWarning(PhotoTooLargeMessage);
            else if (result.Kind == ApiResultKind.ValidationFailed)
                SetWarning(result.Problem?.Detail ?? PhotoTypeMessage);
            else
                SetWarning("Fotoğraf yüklenemedi.");
        }

        return RedirectToAction(nameof(Profile));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> RemovePhoto(CancellationToken ct)
    {
        var result = await photos.DeleteAsync(ct);
        if (result.IsSuccess) SetSuccess("Fotoğrafınız kaldırıldı.");
        else if (result.Kind == ApiResultKind.NotFound) SetWarning("Kaldırılacak bir fotoğraf yok.");
        else SetWarning("Fotoğraf kaldırılamadı.");
        return RedirectToAction(nameof(Profile));
    }

    // Gateway'in göreli /gateway/users/{id}/photo adresi tarayıcıya gitmez; WebUI'nin medya vekili kullanılır.
    // v= : yeni yüklenen fotoğraf 60 sn'lik önbelleğe takılmasın.
    private static string? PhotoUrlFor(Models.Api.UserSummaryApiModel me)
        => string.IsNullOrEmpty(me.PhotoUrl) ? null : $"/media/users/{Uri.EscapeDataString(me.Id)}/photo?v={DateTime.UtcNow.Ticks}";

    [HttpGet, Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost, Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await settings.ChangePasswordAsync(model.CurrentPassword, model.NewPassword, ct);
            if (result.IsSuccess)
            {
                // Eski oturum belirteci süresi dolana dek geçerli kalır; güvenli olan, çıkış yaptırıp yeni şifreyle girişi istemek.
                await HttpContext.SignOutAsync(AuthConstants.CookieScheme);
                SetSuccess("Şifre değişti, yeniden giriş yapın.");
                return RedirectToAction(nameof(Login));
            }

            if (result.Kind == ApiResultKind.Locked)
                ModelState.AddModelError(string.Empty, "Hesap geçici olarak kilitlendi. Lütfen daha sonra tekrar deneyin.");
            else
                ModelState.AddApiProblem(result, "Şifre değiştirilemedi.");
        }

        // Parola alanları (type=password) tag helper tarafından geri yazılmaz: form boş döner.
        return View(model);
    }

    // Çıkış POST: bir bağlantıyla (GET) çıkış yaptırılamasın (başka sitedeki <img src=/Account/Logout> hilesi).
    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthConstants.CookieScheme);
        return RedirectToAction("Index", "Home", new { area = "" });
    }
}
