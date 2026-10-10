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

    public AuthController(IAuthService auth, IUserService users, ICurrentUser currentUser)
    {
        _auth = auth;
        _users = users;
        _currentUser = currentUser;
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

    // traceId gibi her istekte değişen alan eklememek için ProblemDetails elle kurulur: iki 401 gövdesi harfi harfine aynı olsun.
    private static ProblemDetails Fail(int status, string title, string detail)
        => new() { Status = status, Title = title, Detail = detail };
}
