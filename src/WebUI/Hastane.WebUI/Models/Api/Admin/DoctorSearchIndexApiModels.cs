namespace Hastane.WebUI.Models.Api.Admin;

// Doctor servisi GET /api/doctors/search-index/status yanıtının WebUI karşılığı.
public sealed class DoctorSearchIndexStatusApiModel
{
    public bool Enabled { get; set; }
    public bool Reachable { get; set; }
    public string? Index { get; set; }
    public long? IndexedCount { get; set; }
    public long MongoCount { get; set; }
    public bool InSync { get; set; }
}

// POST /api/doctors/search-index/rebuild yanıtı.
public sealed class DoctorSearchIndexRebuildApiModel
{
    public int Indexed { get; set; }
}
