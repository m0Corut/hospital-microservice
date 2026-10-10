using System.Globalization;
using System.Text;
using Hastane.Analytics.Api.Entities;
using Hastane.Analytics.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Hastane.Analytics.Api.Services;

// Doctor servisinin Kafka'ya yazdığı olayın OKUMA tarafı sözleşmesi (kod paylaşılmaz, JSON paylaşılır).
public sealed class ActivityEvent
{
    public Guid EventId { get; set; }
    public string? Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? DoctorId { get; set; }
    public string? Text { get; set; }
    public string? Filters { get; set; }
    public long? ResultCount { get; set; }
    public string? Engine { get; set; }

    public const string SearchType = "search";
    public const string ProfileViewType = "profile_view";
}

// Günlük toplamların "gün" kavramı: İstanbul günü (UTC+3 sabit; Türkiye 2016'dan beri yaz saati uygulamıyor), "yyyy-MM-dd".
public static class AnalyticsDay
{
    private static readonly TimeSpan Istanbul = TimeSpan.FromHours(3);

    public static string Of(DateTimeOffset moment) => moment.ToOffset(Istanbul).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Today() => Of(DateTimeOffset.UtcNow);

    /// <summary>Bugün dahil son <paramref name="days"/> günün ilk günü.</summary>
    public static string FirstDayOfLast(int days) => Of(DateTimeOffset.UtcNow.AddDays(-(days - 1)));

    public static DateTime StartOfTodayUtc()
    {
        var local = DateTimeOffset.UtcNow.ToOffset(Istanbul);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, Istanbul).UtcDateTime;
    }
}

// Arama metnini terime çevirir: küçük harf + Türkçe katlama (İ/I/ı -> i, ç->c, ğ->g, ö->o, ş->s, ü->u), boşluklar tek.
// "Kardiyoloji", "KARDİYOLOJİ", "kardiyoloji " tek terim olur. Boş/çok kısa terim ve kişisel veri izi taşıyanlar (e-posta, uzun sayı) atlanır.
public static class TermNormalizer
{
    public const int MinLength = 3;
    public const int MaxLength = 60;

    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lower = text.Trim().Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant();
        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = ch == 'ı' ? 'i' : ch;
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && sb.Length > 0) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }
            lastWasSpace = false;
            sb.Append(c);
        }

        var term = sb.ToString().Trim();
        if (term.Length < MinLength) return null;
        if (term.Length > MaxLength) term = term[..MaxLength].TrimEnd();

        // Kişisel veri izi: e-posta ya da telefon/TC kimlik gibi uzun rakam dizisi. İstatistiğe girmez.
        if (term.Contains('@')) return null;
        if (term.Count(char.IsDigit) >= 7) return null;
        return term;
    }
}

public enum ProcessResult
{
    Applied,
    Duplicate,
    Ignored,
}

public interface IActivityProcessor
{
    Task<ProcessResult> ProcessAsync(ActivityEvent activity, CancellationToken ct);

    /// <summary>Yeniden hesaplama için tüm toplamları ve işlenmiş-olay kayıtlarını siler; silinen toplam kaydı sayısını döner.</summary>
    Task<long> ClearAllAsync(CancellationToken ct);
}

// "En az bir kez" teslimin bedeli tekrardır; bu sınıf tekrarı etkisiz kılar (idempotent işleme):
//  1) olayın eventId'sini kupon koleksiyonuna _id olarak eklemeyi dene; yinelenen anahtar = bu olay zaten işlendi -> atla.
//  2) kupon alındıysa toplamı $inc ile artır (upsert).
//  3) 2. adım hata verirse kuponu geri sil ve hatayı yukarı at: tüketici offset'i commit etmez, olay yeniden denenir.
// Dürüst not: 1 ile 2 arasında süreç ÇÖKERSE kupon kalır ama sayı artmaz (tek olay eksik sayılır). Mongo çoklu belge işlemi
// replica set ister; bu ölçekte (tek düğüm, analitik sayaç) bu pencere kabul edildi. Tersi sıra (önce say, sonra kupon) çift sayardı.
public sealed class ActivityProcessor : IActivityProcessor
{
    private readonly IMongoCollection<DoctorDailyViews> _views;
    private readonly IMongoCollection<SearchTermDaily> _terms;
    private readonly IMongoCollection<ProcessedEvent> _processed;

    public ActivityProcessor(IMongoClient client, IOptions<DatabaseSettings> options)
    {
        var s = options.Value;
        var db = client.GetDatabase(s.DatabaseName);
        _views = db.GetCollection<DoctorDailyViews>(s.DoctorViewsCollectionName);
        _terms = db.GetCollection<SearchTermDaily>(s.SearchTermsCollectionName);
        _processed = db.GetCollection<ProcessedEvent>(s.ProcessedEventsCollectionName);
    }

    public async Task<ProcessResult> ProcessAsync(ActivityEvent activity, CancellationToken ct)
    {
        if (activity.EventId == Guid.Empty) return ProcessResult.Ignored;

        var day = AnalyticsDay.Of(activity.OccurredAt);
        string? doctorId = null;
        string? term = null;
        switch (activity.Type)
        {
            case ActivityEvent.ProfileViewType:
                doctorId = string.IsNullOrWhiteSpace(activity.DoctorId) ? null : activity.DoctorId.Trim();
                if (doctorId is null) return ProcessResult.Ignored;
                break;
            case ActivityEvent.SearchType:
                term = TermNormalizer.Normalize(activity.Text);
                if (term is null) return ProcessResult.Ignored; // boş / çok kısa arama: terim sayılmaz
                break;
            default:
                return ProcessResult.Ignored;
        }

        var couponId = activity.EventId.ToString("N");
        try
        {
            await _processed.InsertOneAsync(new ProcessedEvent { Id = couponId, At = DateTime.UtcNow }, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return ProcessResult.Duplicate;
        }

        try
        {
            if (doctorId is not null)
            {
                await _views.UpdateOneAsync(
                    Builders<DoctorDailyViews>.Filter.Eq(x => x.Id, $"{doctorId}|{day}"),
                    Builders<DoctorDailyViews>.Update
                        .SetOnInsert(x => x.DoctorId, doctorId)
                        .SetOnInsert(x => x.Day, day)
                        .Inc(x => x.Count, 1),
                    new UpdateOptions { IsUpsert = true }, ct);
            }
            else
            {
                await _terms.UpdateOneAsync(
                    Builders<SearchTermDaily>.Filter.Eq(x => x.Id, $"{term}|{day}"),
                    Builders<SearchTermDaily>.Update
                        .SetOnInsert(x => x.Term, term!)
                        .SetOnInsert(x => x.Day, day)
                        .Inc(x => x.Count, 1),
                    new UpdateOptions { IsUpsert = true }, ct);
            }
        }
        catch
        {
            // Sayım yazılamadı: kuponu geri al ki olay yeniden denendiğinde "tekrar" sanılıp kaybolmasın.
            await _processed.DeleteOneAsync(Builders<ProcessedEvent>.Filter.Eq(x => x.Id, couponId), CancellationToken.None);
            throw;
        }

        return ProcessResult.Applied;
    }

    public async Task<long> ClearAllAsync(CancellationToken ct)
    {
        var views = await _views.DeleteManyAsync(FilterDefinition<DoctorDailyViews>.Empty, ct);
        var terms = await _terms.DeleteManyAsync(FilterDefinition<SearchTermDaily>.Empty, ct);
        await _processed.DeleteManyAsync(FilterDefinition<ProcessedEvent>.Empty, ct);
        return views.DeletedCount + terms.DeletedCount;
    }
}
