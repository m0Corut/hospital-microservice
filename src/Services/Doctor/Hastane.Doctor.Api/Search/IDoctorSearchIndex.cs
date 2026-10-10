using DoctorEntity = Hastane.Doctor.Api.Entities.Doctor;

namespace Hastane.Doctor.Api.Search;

// Doktor ARAMA DİZİNİ (Elasticsearch). Kaynak gerçek MongoDB'dir; bu dizin ondan TÜRETİLMİŞ bir kopyadır ve silinse
// rebuild ile aynen yeniden kurulur. Okuma/yazma metotları hata durumunda fırlatmaz (null / false döner): arama
// dizini düşünce asıl iş (Mongo) etkilenmesin. Yönetim metotları (Rebuild, Count) hatayı SearchIndexException ile bildirir.
public interface IDoctorSearchIndex
{
    /// <summary>Url ayarlı mı? Kapalıysa hiçbir metot dışarıya çıkmaz.</summary>
    bool Enabled { get; }

    /// <summary>Metin araması. Dizin kapalı/hatalı/zaman aşımındaysa null (çağıran Mongo'ya düşer).</summary>
    Task<EsSearchResult?> SearchAsync(DoctorSearchCriteria criteria, int skip, int take, CancellationToken ct);

    /// <summary>Yazdıkça öneri için aday doktorlar. Hata/kapalı: null.</summary>
    Task<IReadOnlyList<SuggestSource>?> SuggestAsync(string text, int take, CancellationToken ct);

    /// <summary>Best-effort: başarılıysa true, atlandı/hata ise false (loglanır).</summary>
    Task<bool> UpsertAsync(DoctorEntity doctor, CancellationToken ct);

    /// <summary>Best-effort: başarılıysa true.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct);

    /// <summary>Toplu yaz (rebuild). Hata: SearchIndexException.</summary>
    Task<int> BulkUpsertAsync(IReadOnlyList<DoctorEntity> doctors, CancellationToken ct);

    /// <summary>Bu zamandan ESKİ dizin belgelerini siler (rebuild sonrası Mongo'da artık olmayan yetim kayıtlar).</summary>
    Task DeleteStaleAsync(DateTime rebuildStartedUtc, CancellationToken ct);

    /// <summary>Yönetim ekranı için: ulaşılabilir mi, kaç belge var. Hata fırlatmaz.</summary>
    Task<SearchIndexCounts> CountAsync(CancellationToken ct);
}
