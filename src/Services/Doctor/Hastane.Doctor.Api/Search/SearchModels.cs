using Hastane.Doctor.Api.Entities;

namespace Hastane.Doctor.Api.Search;

// Elasticsearch ayarları (appsettings "Elasticsearch" bölümü). Url boşsa arama dizini KAPALI: servis yalnız MongoDB ile çalışır.
public class ElasticsearchOptions
{
    public string? Url { get; set; }
    public string Index { get; set; } = "hastane-doctors";

    // İstek zaman aşımı: dizin yavaşsa kullanıcı beklemez, Mongo aramasına düşülür.
    public int TimeoutSeconds { get; set; } = 2;

    // Yazmadan sonra dizini hemen aratılabilir yap (refresh=true). Doktor yazmaları seyrek olduğu için ucuz;
    // yoğun yazmada false yapılırsa değişiklik ~1 sn gecikmeyle görünür.
    public bool RefreshOnWrite { get; set; } = true;

    // Dizin hata verince bu kadar saniye boyunca hiç denenmez (her istekte zaman aşımı beklenmesin).
    public int CircuitBreakSeconds { get; set; } = 30;

    // Rebuild toplu yazma boyutu.
    public int BulkSize { get; set; } = 200;
}

// DoctorService.BuildSearchFilter'ın doğruladığı ve ayrıştırdığı arama ölçütleri: hem Mongo hem Elasticsearch yolu
// AYNI ayrıştırılmış değerleri kullanır, böylece iki yol aynı soruyu sorar.
public sealed record DoctorSearchCriteria
{
    public string? Text { get; init; }
    public DoctorGender? Gender { get; init; }
    public IReadOnlyList<string> BranchIds { get; init; } = [];
    public string? City { get; init; }
    public decimal? MinFee { get; init; }
    public decimal? MaxFee { get; init; }
    public DayOfWeek? Day { get; init; }

    // "name" | "feeasc" | "feedesc" (küçük harf). Boş gelen sıralama "name"e normalleşir; SortGiven ayrımı tutar.
    public string SortKey { get; init; } = "name";
    public bool SortGiven { get; init; }

    public bool HasLocationFilter => City is not null || MinFee is not null || MaxFee is not null || Day is not null;
}

public sealed record EsSearchResult(IReadOnlyList<string> Ids, long Total);

// Öneri için dizinden dönen minimal doktor görünümü.
public sealed record SuggestSource(string Id, string FirstName, string LastName, string? Title, IReadOnlyList<string> Services);

public class SearchIndexException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record SearchIndexCounts(bool Reachable, long? Documents);
