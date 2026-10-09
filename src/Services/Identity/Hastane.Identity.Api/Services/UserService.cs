using Hastane.Identity.Api.Data;
using Hastane.Identity.Api.Dtos;
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
        if (users.Count == 0) return [];

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
