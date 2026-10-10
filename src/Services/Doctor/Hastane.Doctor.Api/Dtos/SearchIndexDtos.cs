namespace Hastane.Doctor.Api.Dtos;

// GET /api/doctors/suggest: yazdıkça öneri. Type: "doctor" (ad soyad, DoctorId dolu) | "service" (hizmet adı).
public class DoctorSuggestionDto
{
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = "doctor";
    public string? DoctorId { get; set; }
}

// GET /api/doctors/search-index/status
public class SearchIndexStatusDto
{
    // Elasticsearch:Url ayarlı mı?
    public bool Enabled { get; set; }
    // Ayarlıysa sunucuya şu an ulaşılabiliyor mu?
    public bool Reachable { get; set; }
    public string? Index { get; set; }
    // Dizindeki belge sayısı (ulaşılamıyorsa null).
    public long? IndexedCount { get; set; }
    // Kaynak gerçek: MongoDB'deki doktor sayısı.
    public long MongoCount { get; set; }
    // İki sayı eşit mi? Değilse yeniden oluşturma önerilir.
    public bool InSync { get; set; }
}

// POST /api/doctors/search-index/rebuild
public class SearchIndexRebuildResultDto
{
    public int Indexed { get; set; }
}
