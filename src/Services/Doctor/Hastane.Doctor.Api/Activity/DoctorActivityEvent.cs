using System.Globalization;
using Hastane.Doctor.Api.Dtos;

namespace Hastane.Doctor.Api.Activity;

// Kafka'ya yazılan olayın sözleşmesi. Analytics servisi aynı JSON'u okur (ProjectReference YOK: sözleşme koddan değil JSON'dan).
// KİŞİSEL VERİ YOK: kullanıcı kimliği, IP, token taşınmaz. Yalnız anonim istatistik: ne arandı, hangi doktor görüntülendi.
// EventId her olayda benzersiz: Kafka "en az bir kez" teslim eder, aynı olay iki kez gelebilir; tüketici EventId ile tekrarı ayıklar.
public sealed record DoctorActivityEvent
{
    public const string SearchType = "search";
    public const string ProfileViewType = "profile_view";

    public Guid EventId { get; init; } = Guid.NewGuid();
    public string Type { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    // profile_view: görüntülenen doktor. Kafka mesaj ANAHTARI de budur (aynı doktorun olayları aynı partition'da, sıralı).
    public string? DoctorId { get; init; }

    // search: arama metni, süzgeç özeti ("city=Ankara;gender=Female"), sonuç sayısı, yanıtlayan motor.
    public string? Text { get; init; }
    public string? Filters { get; init; }
    public long? ResultCount { get; init; }
    public string? Engine { get; init; }

    public static DoctorActivityEvent ProfileView(string doctorId) => new() { Type = ProfileViewType, DoctorId = doctorId };

    public static DoctorActivityEvent Search(DoctorSearchQuery query, long resultCount, string engine) => new()
    {
        Type = SearchType,
        Text = Clip(query.Text, 100),
        Filters = SummarizeFilters(query),
        ResultCount = resultCount,
        Engine = engine,
    };

    // Yalnız hangi süzgeçlerin kullanıldığı (ve kısa değerleri); sıralama/sayfa gibi gürültü yok.
    private static string? SummarizeFilters(DoctorSearchQuery q)
    {
        var parts = new List<string>();
        Add(parts, "gender", q.Gender);
        Add(parts, "branches", q.BranchIds);
        Add(parts, "city", q.City);
        if (q.MinFee is not null || q.MaxFee is not null)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"fee={q.MinFee}-{q.MaxFee}"));
        if (!string.IsNullOrWhiteSpace(q.Date)) parts.Add("date");
        return parts.Count == 0 ? null : string.Join(';', parts);
    }

    private static void Add(List<string> parts, string key, string? value)
    {
        var v = Clip(value, 40);
        if (v is not null) parts.Add($"{key}={v}");
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length <= max ? v : v[..max];
    }
}
