using Hastane.Analytics.Api.Dtos;
using Hastane.Analytics.Api.Entities;
using Hastane.Analytics.Api.Settings;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Hastane.Analytics.Api.Services;

public interface IAnalyticsQueryService
{
    Task<List<TopViewedDoctorDto>> TopViewedDoctorsAsync(int days, int take, CancellationToken ct);
    Task<List<TopSearchTermDto>> TopSearchTermsAsync(int days, int take, CancellationToken ct);
    Task<(DateTime? LastProcessedAt, long ProcessedToday)> ProcessingStatsAsync(CancellationToken ct);
}

// Okuma tarafı: günlük toplamlardan "son N gün" toplamı. Bu, Mongo'daki $match + $group + $sort + $limit boru hattıdır
// (kabukta: db.doctor_views_daily.aggregate([{$match:{Day:{$gte:"2026-10-04"}}},{$group:{_id:"$DoctorId",Total:{$sum:"$Count"}}},{$sort:{Total:-1}},{$limit:10}])).
public sealed class AnalyticsQueryService : IAnalyticsQueryService
{
    private readonly IMongoCollection<DoctorDailyViews> _views;
    private readonly IMongoCollection<SearchTermDaily> _terms;
    private readonly IMongoCollection<ProcessedEvent> _processed;

    public AnalyticsQueryService(IMongoClient client, IOptions<DatabaseSettings> options)
    {
        var s = options.Value;
        var db = client.GetDatabase(s.DatabaseName);
        _views = db.GetCollection<DoctorDailyViews>(s.DoctorViewsCollectionName);
        _terms = db.GetCollection<SearchTermDaily>(s.SearchTermsCollectionName);
        _processed = db.GetCollection<ProcessedEvent>(s.ProcessedEventsCollectionName);
    }

    private sealed class TotalRow
    {
        public string Key { get; set; } = string.Empty;
        public long Total { get; set; }
    }

    public async Task<List<TopViewedDoctorDto>> TopViewedDoctorsAsync(int days, int take, CancellationToken ct)
    {
        var from = AnalyticsDay.FirstDayOfLast(days);
        var rows = await _views.Aggregate()
            // Gün "yyyy-MM-dd" metni: sözlük sırası = tarih sırası, $gte doğrudan çalışır (LINQ string.Compare çevrilemiyor).
            .Match(Builders<DoctorDailyViews>.Filter.Gte(x => x.Day, from))
            .Group(x => x.DoctorId, g => new TotalRow { Key = g.Key, Total = g.Sum(x => x.Count) })
            .SortByDescending(r => r.Total).ThenBy(r => r.Key)
            .Limit(take)
            .ToListAsync(ct);
        return rows.Select(r => new TopViewedDoctorDto { DoctorId = r.Key, Views = r.Total }).ToList();
    }

    public async Task<List<TopSearchTermDto>> TopSearchTermsAsync(int days, int take, CancellationToken ct)
    {
        var from = AnalyticsDay.FirstDayOfLast(days);
        var rows = await _terms.Aggregate()
            .Match(Builders<SearchTermDaily>.Filter.Gte(x => x.Day, from))
            .Group(x => x.Term, g => new TotalRow { Key = g.Key, Total = g.Sum(x => x.Count) })
            .SortByDescending(r => r.Total).ThenBy(r => r.Key)
            .Limit(take)
            .ToListAsync(ct);
        return rows.Select(r => new TopSearchTermDto { Term = r.Key, Count = r.Total }).ToList();
    }

    public async Task<(DateTime? LastProcessedAt, long ProcessedToday)> ProcessingStatsAsync(CancellationToken ct)
    {
        var last = await _processed.Find(FilterDefinition<ProcessedEvent>.Empty)
            .SortByDescending(x => x.At).Limit(1).FirstOrDefaultAsync(ct);
        var startOfToday = AnalyticsDay.StartOfTodayUtc();
        var today = await _processed.CountDocumentsAsync(Builders<ProcessedEvent>.Filter.Gte(x => x.At, startOfToday), cancellationToken: ct);
        return (last?.At, today);
    }
}
