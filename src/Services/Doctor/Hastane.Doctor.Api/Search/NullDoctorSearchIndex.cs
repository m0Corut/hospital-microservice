using DoctorEntity = Hastane.Doctor.Api.Entities.Doctor;

namespace Hastane.Doctor.Api.Search;

// Elasticsearch:Url boşken kullanılır (varsayılan, testler): hiçbir şey yapmaz, arama doğrudan Mongo'ya gider.
public sealed class NullDoctorSearchIndex : IDoctorSearchIndex
{
    public bool Enabled => false;
    public Task<EsSearchResult?> SearchAsync(DoctorSearchCriteria criteria, int skip, int take, CancellationToken ct) => Task.FromResult<EsSearchResult?>(null);
    public Task<IReadOnlyList<SuggestSource>?> SuggestAsync(string text, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<SuggestSource>?>(null);
    public Task<bool> UpsertAsync(DoctorEntity doctor, CancellationToken ct) => Task.FromResult(false);
    public Task<bool> DeleteAsync(string id, CancellationToken ct) => Task.FromResult(false);
    public Task<int> BulkUpsertAsync(IReadOnlyList<DoctorEntity> doctors, CancellationToken ct) => Task.FromResult(0);
    public Task DeleteStaleAsync(DateTime rebuildStartedUtc, CancellationToken ct) => Task.CompletedTask;
    public Task<SearchIndexCounts> CountAsync(CancellationToken ct) => Task.FromResult(new SearchIndexCounts(false, null));
}
