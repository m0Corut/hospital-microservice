using Hastane.Identity.Api.Dtos;

namespace Hastane.Identity.Api.Services;

public interface IUserService
{
    // Bozuk ya da bulunamayan id için null.
    Task<UserSummaryDto?> GetByIdAsync(string id, CancellationToken ct);

    // Bulunamayanlar sessizce atlanır; sıra garanti değil.
    Task<List<UserSummaryDto>> GetByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken ct);
}
