using Hastane.Branch.Api.Dtos;

namespace Hastane.Branch.Api.Services;

public interface IBranchService
{
    Task<List<ResultBranchDto>> GetAllAsync(CancellationToken ct);
    Task<GetByIdBranchDto?> GetByIdAsync(string id, CancellationToken ct);
    Task<ResultBranchDto> CreateAsync(CreateBranchDto dto, CancellationToken ct);
    /// <summary>Kayıt yoksa false döner.</summary>
    Task<bool> UpdateAsync(UpdateBranchDto dto, CancellationToken ct);
    /// <summary>Kayıt yoksa false döner.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct);
}
