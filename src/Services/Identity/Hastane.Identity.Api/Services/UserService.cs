using Hastane.Identity.Api.Data;
using Hastane.Identity.Api.Dtos;
using Hastane.Identity.Api.Entities;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Identity.Api.Services;

public class UserService : IUserService
{
    private readonly HastaneIdentityDbContext _db;
    private readonly IMapper _mapper;

    public UserService(HastaneIdentityDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<UserSummaryDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        // Identity id'leri Guid metnidir; Guid olmayan bir şey veritabanına hiç gitmeden "yok" sayılır.
        if (!Guid.TryParse(id, out var guid)) return null;
        var list = await GetByIdsAsync([guid.ToString()], ct);
        return list.FirstOrDefault();
    }

    public async Task<List<UserSummaryDto>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var idList = ids.ToList();

        var users = await _db.Users.AsNoTracking()
            .Where(u => idList.Contains(u.Id))
            .ToListAsync(ct);
        return await ToSummariesAsync(users, ct);
    }

    public async Task<PagedResult<UserSummaryDto>> ListAsync(int page, int pageSize, string? search, string? role,
        CancellationToken ct)
    {
        IQueryable<AppUser> query = _db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalized = role.Trim().ToUpperInvariant();
            // Bilinmeyen rol hata değil, boş sonuç: süzme "bu role sahip olanlar" demektir, kimse yok.
            query = query.Where(u => _db.UserRoles.Any(ur =>
                ur.UserId == u.Id && _db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == normalized)));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            // Contains -> LIKE '%term%' (joker karakterler EF tarafından kaçırılır); SQL Server karşılaştırması büyük/küçük harf duyarsız.
            query = query.Where(u => (u.Email != null && u.Email.Contains(term))
                || u.FirstName.Contains(term) || u.LastName.Contains(term)
                || (u.FirstName + " " + u.LastName).Contains(term));
        }

        var total = await query.CountAsync(ct);
        var users = await query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<UserSummaryDto>
        {
            Items = await ToSummariesAsync(users, ct),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<UserSummaryDto?> UpdateProfileAsync(string id, UpdateProfileDto dto, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var guid)) return null;
        var key = guid.ToString();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == key, ct);
        if (user is null) return null;

        user.FirstName = dto.FirstName.Trim();
        user.LastName = dto.LastName.Trim();
        await _db.SaveChangesAsync(ct);
        return await GetByIdAsync(key, ct);
    }

    private async Task<List<UserSummaryDto>> ToSummariesAsync(List<AppUser> users, CancellationToken ct)
    {
        if (users.Count == 0) return [];
        var idList = users.Select(u => u.Id).ToList();

        // Kullanıcı başına ayrı rol sorgusu (N+1) yerine tek sorguda hepsinin rolleri.
        var roleRows = await (
                from ur in _db.UserRoles.AsNoTracking()
                join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where idList.Contains(ur.UserId)
                select new { ur.UserId, RoleName = r.Name })
            .ToListAsync(ct);
        var rolesByUser = roleRows
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RoleName!).Order().ToList());

        return users.Select(u =>
        {
            var dto = _mapper.Map<UserSummaryDto>(u);
            dto.Roles = rolesByUser.GetValueOrDefault(u.Id) ?? [];
            return dto;
        }).ToList();
    }
}
