using System.Data;
using Hastane.Identity.Api.Data;
using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Entities;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Identity.Api.Services;

public class RoleService : IRoleService
{
    private readonly HastaneIdentityDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IUserService _userService;

    public RoleService(HastaneIdentityDbContext db, UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager, IUserService userService)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _userService = userService;
    }

    public async Task<List<RoleDto>> ListRolesAsync(CancellationToken ct)
        => await _db.Roles.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new RoleDto { Name = r.Name! }).ToListAsync(ct);

    public async Task<RoleCreateStatus> CreateRoleAsync(string name, CancellationToken ct)
    {
        name = name.Trim();
        // RoleExistsAsync adı büyük/küçük harf duyarsız karşılaştırır ("admin" == "Admin").
        if (await _roleManager.RoleExistsAsync(name)) return RoleCreateStatus.AlreadyExists;

        var result = await _roleManager.CreateAsync(new IdentityRole(name));
        if (result.Succeeded) return RoleCreateStatus.Created;
        // Aynı anda iki istek: kaybeden DuplicateRoleName alır.
        return await _roleManager.RoleExistsAsync(name) ? RoleCreateStatus.AlreadyExists : RoleCreateStatus.Invalid;
    }

    public async Task<UserOpOutcome> AddToRoleAsync(string userId, string role, CancellationToken ct)
    {
        var user = await FindUserAsync(userId);
        if (user is null) return new UserOpOutcome(UserOpStatus.UserNotFound);
        var identityRole = await _roleManager.FindByNameAsync(role.Trim());
        if (identityRole is null) return new UserOpOutcome(UserOpStatus.RoleNotFound);

        if (await _userManager.IsInRoleAsync(user, identityRole.Name!))
            return new UserOpOutcome(UserOpStatus.AlreadyInRole);

        var result = await _userManager.AddToRoleAsync(user, identityRole.Name!);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Rol atanamadı: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        return await OkAsync(user.Id, ct);
    }

    public async Task<UserOpOutcome> RemoveFromRoleAsync(string userId, string role, CancellationToken ct)
    {
        var user = await FindUserAsync(userId);
        if (user is null) return new UserOpOutcome(UserOpStatus.UserNotFound);
        var identityRole = await _roleManager.FindByNameAsync(role.Trim());
        if (identityRole is null) return new UserOpOutcome(UserOpStatus.RoleNotFound);
        var roleName = identityRole.Name!;

        // Say + sil tek serializable işlemde: iki Admin aynı anda birbirini düşürüp sistemi Admin'siz bırakamasın.
        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        if (!await _userManager.IsInRoleAsync(user, roleName))
            return new UserOpOutcome(UserOpStatus.NotInRole);

        if (roleName == HastaneRoles.Admin)
        {
            var adminCount = await _db.UserRoles.CountAsync(ur => ur.RoleId == identityRole.Id, ct);
            // Son Admin (kendini düşüren Admin dahil) düşürülemez: Admin'i atayacak kimse kalmazdı.
            if (adminCount <= 1) return new UserOpOutcome(UserOpStatus.LastAdmin);
        }

        var result = await _userManager.RemoveFromRoleAsync(user, roleName);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Rol kaldırılamadı: {string.Join("; ", result.Errors.Select(e => e.Description))}");

        // Doctor rolü gidince doktor bağı da çözülür: token'da doctor_id kalmaz, doktor kaydı başkasına bağlanabilir.
        if (roleName == HastaneRoles.Doctor && user.DoctorId is not null)
        {
            user.DoctorId = null;
            var update = await _userManager.UpdateAsync(user);
            if (!update.Succeeded)
                throw new InvalidOperationException("Doktor bağı çözülemedi.");
        }

        await tx.CommitAsync(ct);
        return await OkAsync(user.Id, ct);
    }

    public async Task<UserOpOutcome> LinkDoctorAsync(string userId, string doctorId, CancellationToken ct)
    {
        var user = await FindUserAsync(userId);
        if (user is null) return new UserOpOutcome(UserOpStatus.UserNotFound);
        doctorId = doctorId.Trim();

        if (!await _userManager.IsInRoleAsync(user, HastaneRoles.Doctor))
            return new UserOpOutcome(UserOpStatus.NotADoctor);

        if (await _db.Users.AnyAsync(u => u.DoctorId == doctorId && u.Id != user.Id, ct))
            return new UserOpOutcome(UserOpStatus.DoctorAlreadyLinked);

        user.DoctorId = doctorId;
        try
        {
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Doktor bağlanamadı: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
        catch (DbUpdateException)
        {
            // Kontrol ile yazma arasında başka istek aynı doktoru bağladıysa filtreli benzersiz indeks reddeder.
            _db.ChangeTracker.Clear();
            if (await _db.Users.AsNoTracking().AnyAsync(u => u.DoctorId == doctorId && u.Id != user.Id, ct))
                return new UserOpOutcome(UserOpStatus.DoctorAlreadyLinked);
            throw;
        }

        return await OkAsync(user.Id, ct);
    }

    public async Task<UserOpOutcome> UnlinkDoctorAsync(string userId, CancellationToken ct)
    {
        var user = await FindUserAsync(userId);
        if (user is null) return new UserOpOutcome(UserOpStatus.UserNotFound);

        if (user.DoctorId is not null)
        {
            user.DoctorId = null;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Doktor bağı çözülemedi.");
        }

        return await OkAsync(user.Id, ct);
    }

    // Identity id'leri Guid metnidir; bozuk id veritabanına hiç gitmeden "yok".
    private async Task<AppUser?> FindUserAsync(string id)
        => Guid.TryParse(id, out var guid) ? await _userManager.FindByIdAsync(guid.ToString()) : null;

    private async Task<UserOpOutcome> OkAsync(string userId, CancellationToken ct)
        => new(UserOpStatus.Ok, await _userService.GetByIdAsync(userId, ct));
}
