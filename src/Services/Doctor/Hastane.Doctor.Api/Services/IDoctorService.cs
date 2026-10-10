using Hastane.Doctor.Api.Dtos;

namespace Hastane.Doctor.Api.Services;

public interface IDoctorService
{
    Task<List<ResultDoctorDto>> GetAllAsync(CancellationToken ct);
    Task<List<ResultDoctorDto>> GetByBranchIdAsync(string branchId, CancellationToken ct);
    Task<GetByIdDoctorDto?> GetByIdAsync(string id, CancellationToken ct);
    /// <summary>Herkese açık profil: telefon ve e-posta YOK. Bozuk id ya da kayıt yoksa null.</summary>
    Task<PublicDoctorDto?> GetPublicByIdAsync(string id, CancellationToken ct);
    /// <summary>Geçersiz girdide DoctorValidationException fırlatır (controller 400'e çevirir).</summary>
    Task<ResultDoctorDto> CreateAsync(CreateDoctorDto dto, CancellationToken ct);
    /// <summary>Kayıt yoksa false döner. Geçersiz girdide DoctorValidationException.</summary>
    Task<bool> UpdateAsync(UpdateDoctorDto dto, CancellationToken ct);
    /// <summary>Kayıt yoksa false döner.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct);

    /// <summary>Sayfalı arama. Geçersiz parametrede DoctorValidationException.</summary>
    Task<PagedResult<DoctorSearchItemDto>> SearchAsync(DoctorSearchQuery query, CancellationToken ct);

    /// <summary>Arama + hangi motorun yanıtladığı ("elasticsearch" | "mongo"; X-Search-Engine başlığı için).</summary>
    Task<(PagedResult<DoctorSearchItemDto> Result, string Engine)> SearchWithEngineAsync(DoctorSearchQuery query, CancellationToken ct);

    /// <summary>Yazdıkça öneri (en çok 8): ad/soyad/hizmet. Boş q: boş liste. Çok uzun q: DoctorValidationException.</summary>
    Task<List<DoctorSuggestionDto>> SuggestAsync(string? q, CancellationToken ct);

    /// <summary>Tüm doktorları Mongo'dan arama dizinine yeniden yazar; yazılan sayı. Dizin kapalıysa null. Dizin hatasında SearchIndexException.</summary>
    Task<int?> RebuildSearchIndexAsync(CancellationToken ct);

    Task<SearchIndexStatusDto> GetSearchIndexStatusAsync(CancellationToken ct);

    /// <summary>Virgülle ayrılmış id'lerle toplu özet. Geçersiz id atılır, tekrarlar silinir, en çok 100 id.</summary>
    Task<List<DoctorSummaryDto>> GetBatchAsync(string? ids, CancellationToken ct);
}
