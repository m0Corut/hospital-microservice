using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Entities;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Identity.Api.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly IUserService _userService;
    private readonly IJwtTokenService _tokens;

    public AuthService(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager,
        IUserService userService, IJwtTokenService tokens)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _userService = userService;
        _tokens = tokens;
    }

    public async Task<RegisterOutcome> RegisterAsync(RegisterDto dto, CancellationToken ct)
    {
        var email = dto.Email.Trim();
        var user = new AppUser
        {
            // Kullanıcı adı = e-posta; giriş e-postayla yapılır.
            UserName = email,
            Email = email,
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            CreatedAt = DateTime.UtcNow,
        };

        IdentityResult created;
        try
        {
            // Şifre burada hash + salt ile saklanır; düz metin hiçbir yere yazılmaz.
            created = await _userManager.CreateAsync(user, dto.Password);
        }
        catch (DbUpdateException)
        {
            // İki istek aynı anda aynı e-postayla gelirse doğrulamayı ikisi de geçer; benzersiz indeks ikincisini reddeder.
            if (await _userManager.FindByEmailAsync(email) is not null)
                return new RegisterOutcome(RegisterStatus.EmailTaken);
            throw;
        }

        if (!created.Succeeded)
        {
            var taken = created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                or nameof(IdentityErrorDescriber.DuplicateUserName));
            return taken
                ? new RegisterOutcome(RegisterStatus.EmailTaken)
                : new RegisterOutcome(RegisterStatus.Invalid, Errors: created.Errors.ToList());
        }

        // Rol istemciden gelmez: kayıt olan HER ZAMAN Patient.
        var roleResult = await _userManager.AddToRoleAsync(user, HastaneRoles.Patient);
        if (!roleResult.Succeeded)
        {
            // Rolsüz yarım kullanıcı kalmasın.
            await _userManager.DeleteAsync(user);
            throw new InvalidOperationException(
                $"Patient rolü atanamadı: {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
        }

        var summary = await _userService.GetByIdAsync(user.Id, ct)
            ?? throw new InvalidOperationException("Yeni kullanıcı okunamadı.");
        return new RegisterOutcome(RegisterStatus.Created, summary);
    }

    public async Task<LoginOutcome> LoginAsync(LoginDto dto, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email.Trim());
        // Kullanıcı yok ile şifre yanlış AYNI sonucu verir (kullanıcı numaralandırmayı önlemek için).
        if (user is null) return new LoginOutcome(LoginStatus.InvalidCredentials);

        // lockoutOnFailure: true -> her hatalı denemede sayaç artar, 5'te hesap 5 dk kilitlenir.
        var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (result.IsLockedOut) return new LoginOutcome(LoginStatus.LockedOut);
        if (!result.Succeeded) return new LoginOutcome(LoginStatus.InvalidCredentials);

        var summary = await _userService.GetByIdAsync(user.Id, ct)
            ?? throw new InvalidOperationException("Giriş yapan kullanıcı okunamadı.");
        // Roller ve doktor bağı veritabanından HER girişte taze okunur; token bu andaki yetkiyi taşır.
        var token = _tokens.CreateAccessToken(summary);
        return new LoginOutcome(LoginStatus.Success, new LoginResultDto
        {
            AccessToken = token.Value,
            ExpiresAt = token.ExpiresAt,
            User = summary,
        });
    }
}
