using Hastane.Identity.Api.Data;
using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Entities;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Identity.Api.Services;

public enum RegisterDoctorStatus { Created, EmailTaken, Invalid }

public record RegisterDoctorOutcome(RegisterDoctorStatus Status, DoctorApplicationDto? Application = null,
    IReadOnlyList<IdentityError>? Errors = null);

public enum DecisionStatus { Decided, NotFound, AlreadyDecided }

public record DecisionOutcome(DecisionStatus Status, DoctorApplicationDto? Application = null);

public interface IDoctorApplicationService
{
    Task<RegisterDoctorOutcome> RegisterAsync(RegisterDoctorDto dto, CancellationToken ct);
    Task<PagedResult<DoctorApplicationDto>> ListAsync(DoctorApplicationStatus? status, int page, int pageSize, CancellationToken ct);
    Task<DoctorApplicationDto?> GetForUserAsync(string userId, CancellationToken ct);
    Task<DecisionOutcome> ApproveAsync(Guid id, string decidedBy, CancellationToken ct);
    Task<DecisionOutcome> RejectAsync(Guid id, string decidedBy, string note, CancellationToken ct);
}

public class DoctorApplicationService(HastaneIdentityDbContext db, IAuthService auth, UserManager<AppUser> userManager,
    TimeProvider time) : IDoctorApplicationService
{
    public async Task<RegisterDoctorOutcome> RegisterAsync(RegisterDoctorDto dto, CancellationToken ct)
    {
        // Kullanıcı + başvuru TEK işlemde: başvurusuz "yarım" kullanıcı kalmasın.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var registered = await auth.RegisterAsync(new RegisterDto
        {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            Password = dto.Password,
        }, ct);
        if (registered.Status == RegisterStatus.EmailTaken) return new RegisterDoctorOutcome(RegisterDoctorStatus.EmailTaken);
        if (registered.Status != RegisterStatus.Created)
            return new RegisterDoctorOutcome(RegisterDoctorStatus.Invalid, Errors: registered.Errors);

        var application = new DoctorApplication
        {
            Id = Guid.NewGuid(),
            UserId = registered.User!.Id,
            BranchId = string.IsNullOrWhiteSpace(dto.BranchId) ? null : dto.BranchId.Trim(),
            LicenseNumber = dto.LicenseNumber.Trim(),
            Note = dto.Note?.Trim() ?? string.Empty,
            Status = DoctorApplicationStatus.Pending,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.DoctorApplications.Add(application);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new RegisterDoctorOutcome(RegisterDoctorStatus.Created, ToDto(application, registered.User.FirstName,
            registered.User.LastName, registered.User.Email));
    }

    public async Task<PagedResult<DoctorApplicationDto>> ListAsync(DoctorApplicationStatus? status, int page, int pageSize,
        CancellationToken ct)
    {
        var query = db.DoctorApplications.AsNoTracking().AsQueryable();
        if (status is not null) query = query.Where(a => a.Status == status.Value);

        var total = await query.CountAsync(ct);
        var rows = await (from a in query
                          join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                          select new { Application = a, u.FirstName, u.LastName, u.Email })
            .OrderByDescending(x => x.Application.CreatedAt).ThenBy(x => x.Application.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<DoctorApplicationDto>
        {
            Items = rows.Select(r => ToDto(r.Application, r.FirstName, r.LastName, r.Email ?? string.Empty)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<DoctorApplicationDto?> GetForUserAsync(string userId, CancellationToken ct)
        => await LoadAsync(a => a.UserId == userId, ct);

    public async Task<DecisionOutcome> ApproveAsync(Guid id, string decidedBy, CancellationToken ct)
    {
        // Karar + rol değişimi TEK işlemde (atomik): ya hepsi olur ya hiçbiri.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var claimed = await ClaimAsync(id, DoctorApplicationStatus.Approved, decidedBy, null, ct);
        if (claimed != DecisionStatus.Decided) return new DecisionOutcome(claimed);

        var userId = await db.DoctorApplications.Where(a => a.Id == id).Select(a => a.UserId).SingleAsync(ct);
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return new DecisionOutcome(DecisionStatus.NotFound); // tx geri alınır

        if (!await userManager.IsInRoleAsync(user, HastaneRoles.Doctor))
            Check(await userManager.AddToRoleAsync(user, HastaneRoles.Doctor), "Doctor rolü atanamadı");
        if (await userManager.IsInRoleAsync(user, HastaneRoles.Patient))
            Check(await userManager.RemoveFromRoleAsync(user, HastaneRoles.Patient), "Patient rolü kaldırılamadı");

        await tx.CommitAsync(ct);
        return new DecisionOutcome(DecisionStatus.Decided, await LoadAsync(a => a.Id == id, ct));
    }

    public async Task<DecisionOutcome> RejectAsync(Guid id, string decidedBy, string note, CancellationToken ct)
    {
        var claimed = await ClaimAsync(id, DoctorApplicationStatus.Rejected, decidedBy, note.Trim(), ct);
        return claimed == DecisionStatus.Decided
            ? new DecisionOutcome(DecisionStatus.Decided, await LoadAsync(a => a.Id == id, ct))
            : new DecisionOutcome(claimed);
    }

    // "Yalnız Pending ise" koşullu tek UPDATE: iki Admin aynı anda karar verirse yalnız biri satırı değiştirir (öbürü 0 satır -> 409).
    private async Task<DecisionStatus> ClaimAsync(Guid id, DoctorApplicationStatus target, string decidedBy, string? note,
        CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var rows = await db.DoctorApplications
            .Where(a => a.Id == id && a.Status == DoctorApplicationStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, target)
                .SetProperty(a => a.DecidedAt, now)
                .SetProperty(a => a.DecidedBy, decidedBy)
                .SetProperty(a => a.DecisionNote, note), ct);
        if (rows == 1) return DecisionStatus.Decided;

        return await db.DoctorApplications.AnyAsync(a => a.Id == id, ct) ? DecisionStatus.AlreadyDecided : DecisionStatus.NotFound;
    }

    private async Task<DoctorApplicationDto?> LoadAsync(System.Linq.Expressions.Expression<Func<DoctorApplication, bool>> filter,
        CancellationToken ct)
    {
        var row = await (from a in db.DoctorApplications.AsNoTracking().Where(filter)
                         join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                         select new { Application = a, u.FirstName, u.LastName, u.Email })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToDto(row.Application, row.FirstName, row.LastName, row.Email ?? string.Empty);
    }

    private static void Check(IdentityResult result, string message)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{message}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
    }

    private static DoctorApplicationDto ToDto(DoctorApplication a, string firstName, string lastName, string email) => new()
    {
        Id = a.Id,
        UserId = a.UserId,
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        BranchId = a.BranchId,
        LicenseNumber = a.LicenseNumber,
        Note = a.Note,
        Status = a.Status.ToString(),
        CreatedAt = a.CreatedAt,
        DecidedAt = a.DecidedAt,
        DecidedBy = a.DecidedBy,
        DecisionNote = a.DecisionNote,
    };
}
