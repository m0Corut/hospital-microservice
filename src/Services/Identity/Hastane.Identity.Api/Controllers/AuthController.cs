using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
// Varsayılan kapalı: her uç giriş ister, açık olanlar [AllowAnonymous] ile bilerek işaretlenir.
[Authorize(Policy = HastanePolicies.AuthenticatedUser)]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;
    private readonly ICurrentUser _currentUser;
    private readonly IPasswordResetService _passwordReset;
    private readonly IDoctorApplicationService _doctorApplications;
    private readonly IPhotoService _photos;

    // Çok parçalı gövde, dosya sınırı (2 MB) + başlık/sınır payı. Uç düzeyinde: Kestrel bunu aşan gövdeyi 413 ile keser.
    private const int MaxPhotoRequestBytes = PhotoService.MaxBytes + 64 * 1024;

    public AuthController(IAuthService auth, IUserService users, ICurrentUser currentUser,
        IPasswordResetService passwordReset, IDoctorApplicationService doctorApplications, IPhotoService photos)
    {
        _auth = auth;
        _users = users;
        _currentUser = currentUser;
        _passwordReset = passwordReset;
        _doctorApplications = doctorApplications;
        _photos = photos;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<UserSummaryDto>> Register(RegisterDto dto, CancellationToken ct)
    {
        var outcome = await _auth.RegisterAsync(dto, ct);
        switch (outcome.Status)
        {
            case RegisterStatus.Created:
                return CreatedAtAction(nameof(UsersController.GetById), "Users",
                    new { id = outcome.User!.Id }, outcome.User);

            case RegisterStatus.EmailTaken:
                return Problem(statusCode: StatusCodes.Status409Conflict, title: "Çakışma",
                    detail: "Bu e-posta adresi zaten kayıtlı.");

            default:
                // Identity hata kodları (PasswordTooShort, PasswordRequiresDigit...) doğrulama hatası biçiminde döner.
                foreach (var error in outcome.Errors!)
                    ModelState.AddModelError(error.Code, error.Description);
                return ValidationProblem(ModelState);
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResultDto>> Login(LoginDto dto, CancellationToken ct)
    {
        var outcome = await _auth.LoginAsync(dto, ct);
        return outcome.Status switch
        {
            LoginStatus.Success => Ok(outcome.Result),
            LoginStatus.LockedOut => StatusCode(StatusCodes.Status423Locked, Fail(StatusCodes.Status423Locked,
                "Hesap geçici olarak kilitlendi", "Çok fazla başarısız deneme. Lütfen daha sonra tekrar deneyin.")),
            // "Kullanıcı yok" ve "şifre yanlış" için birebir aynı gövde.
            _ => Unauthorized(Fail(StatusCodes.Status401Unauthorized,
                "Giriş başarısız", "E-posta veya şifre hatalı.")),
        };
    }

    // Token'daki kullanıcının GÜNCEL özeti (roller token çıkarıldıktan sonra değişmiş olabilir; veritabanından okunur).
    [HttpGet("me")]
    public async Task<ActionResult<UserSummaryDto>> Me(CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId ?? string.Empty, ct);
        return user is null ? NotFound() : Ok(user);
    }

    // Id istekten DEĞİL token'dan gelir: kimse başkasının profilini güncelleyemez. Giriş yapmış herkes.
    [HttpPut("me")]
    public async Task<ActionResult<UserSummaryDto>> UpdateMe(UpdateProfileDto dto, CancellationToken ct)
    {
        var user = await _users.UpdateProfileAsync(_currentUser.UserId ?? string.Empty, dto, ct);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto, CancellationToken ct)
    {
        var outcome = await _auth.ChangePasswordAsync(_currentUser.UserId ?? string.Empty, dto, ct);
        switch (outcome.Status)
        {
            case ChangePasswordStatus.Success:
                return NoContent();
            case ChangePasswordStatus.UserNotFound:
                return NotFound();
            case ChangePasswordStatus.LockedOut:
                return StatusCode(StatusCodes.Status423Locked, Fail(StatusCodes.Status423Locked,
                    "Hesap geçici olarak kilitlendi", "Çok fazla başarısız deneme. Lütfen daha sonra tekrar deneyin."));
            case ChangePasswordStatus.WrongCurrentPassword:
                // Genel mesaj: kalan deneme sayısı gibi ayrıntı verilmez.
                return BadRequest(Fail(StatusCodes.Status400BadRequest, "Şifre değiştirilemedi",
                    "Mevcut şifre hatalı."));
            case ChangePasswordStatus.SamePassword:
                ModelState.AddModelError(nameof(ChangePasswordDto.NewPassword), "Yeni şifre mevcut şifreyle aynı olamaz.");
                return ValidationProblem(ModelState);
            default:
                foreach (var error in outcome.Errors!)
                    ModelState.AddModelError(error.Code, error.Description);
                return ValidationProblem(ModelState);
        }
    }

    // HER ZAMAN 202 ve aynı gövde: kullanıcı var/yok, hız sınırı aşıldı/aşılmadı dışarıdan ayırt edilemez.
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto, CancellationToken ct)
    {
        await _passwordReset.RequestResetAsync(dto.Email, ct);
        return StatusCode(StatusCodes.Status202Accepted,
            new MessageDto("Bu e-posta adresi kayıtlıysa şifre sıfırlama bağlantısı gönderildi."));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto, CancellationToken ct)
    {
        var outcome = await _passwordReset.ResetAsync(dto, ct);
        switch (outcome.Status)
        {
            case ResetPasswordStatus.Success:
                return NoContent();
            case ResetPasswordStatus.WeakPassword:
                foreach (var error in outcome.Errors!)
                    ModelState.AddModelError(error.Code, error.Description);
                return ValidationProblem(ModelState);
            default:
                // Geçersiz / süresi geçmiş / kullanılmış token ve olmayan kullanıcı için birebir aynı gövde.
                return BadRequest(Fail(StatusCodes.Status400BadRequest, "Şifre sıfırlanamadı",
                    "Bağlantı geçersiz ya da süresi dolmuş. Lütfen yeni bir sıfırlama bağlantısı isteyin."));
        }
    }

    [HttpPost("register-doctor")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterDoctor(RegisterDoctorDto dto, CancellationToken ct)
    {
        var outcome = await _doctorApplications.RegisterAsync(dto, ct);
        switch (outcome.Status)
        {
            case RegisterDoctorStatus.Created:
                return Created("/api/auth/me/doctor-application", outcome.Application);
            case RegisterDoctorStatus.EmailTaken:
                return Problem(statusCode: StatusCodes.Status409Conflict, title: "Çakışma",
                    detail: "Bu e-posta adresi zaten kayıtlı.");
            default:
                foreach (var error in outcome.Errors!)
                    ModelState.AddModelError(error.Code, error.Description);
                return ValidationProblem(ModelState);
        }
    }

    // Kullanıcı yalnız KENDİ başvurusunu görür (id token'dan); başvuru yoksa 404.
    [HttpGet("me/doctor-application")]
    public async Task<ActionResult<DoctorApplicationDto>> MyDoctorApplication(CancellationToken ct)
    {
        var application = await _doctorApplications.GetForUserAsync(_currentUser.UserId ?? string.Empty, ct);
        return application is null ? NotFound() : Ok(application);
    }

    // Hedef kullanıcı token'dan gelir ("me"): başkasının fotoğrafı bu uçlardan değiştirilemez/silinemez.
    [HttpPut("me/photo")]
    [RequestSizeLimit(MaxPhotoRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPhotoRequestBytes)]
    public async Task<ActionResult<UserSummaryDto>> UploadPhoto(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Geçersiz dosya",
                detail: "'file' alanında bir görsel gönderin.");
        if (file.Length > PhotoService.MaxBytes)
            return Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Dosya çok büyük",
                detail: "Fotoğraf en çok 2 MB olabilir.");

        // file.FileName / ContentType hiç kullanılmaz: tür içerik imzasından, ad sunucudan.
        await using var stream = file.OpenReadStream();
        var status = await _photos.SaveAsync(_currentUser.UserId ?? string.Empty, stream, ct);
        switch (status)
        {
            case PhotoSaveStatus.Saved:
                return Ok(await _users.GetByIdAsync(_currentUser.UserId ?? string.Empty, ct));
            case PhotoSaveStatus.TooLarge:
                return Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Dosya çok büyük",
                    detail: "Fotoğraf en çok 2 MB olabilir.");
            case PhotoSaveStatus.UnsupportedType:
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Desteklenmeyen dosya",
                    detail: "Yalnız PNG, JPEG ya da WebP görseller kabul edilir.");
            default:
                return NotFound();
        }
    }

    [HttpDelete("me/photo")]
    public async Task<IActionResult> DeletePhoto(CancellationToken ct)
        => await _photos.DeleteAsync(_currentUser.UserId ?? string.Empty, ct) ? NoContent() : NotFound();

    // traceId gibi her istekte değişen alan eklememek için ProblemDetails elle kurulur: iki 401 gövdesi harfi harfine aynı olsun.
    private static ProblemDetails Fail(int status, string title, string detail)
        => new() { Status = status, Title = title, Detail = detail };
}
