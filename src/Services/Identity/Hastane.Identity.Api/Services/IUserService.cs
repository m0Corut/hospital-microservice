using Hastane.Identity.Api.Dtos;

namespace Hastane.Identity.Api.Services;

public interface IUserService
{
    // Bozuk ya da bulunamayan id için null.
    Task<UserSummaryDto?> GetByIdAsync(string id, CancellationToken ct);

    // Bulunamayanlar sessizce atlanır; sıra garanti değil.
    Task<List<UserSummaryDto>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct);

    // Admin listesi: sayfalı, e-posta / ad araması, rol süzmesi. Roller sayfa için TEK sorguda toplanır.
    Task<PagedResult<UserSummaryDto>> ListAsync(int page, int pageSize, string? search, string? role, CancellationToken ct);

    // Yalnız ad ve soyadı günceller; kullanıcı yoksa null.
    Task<UserSummaryDto?> UpdateProfileAsync(string id, UpdateProfileDto dto, CancellationToken ct);
}
