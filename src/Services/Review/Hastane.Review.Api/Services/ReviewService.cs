using Hastane.Review.Api.Dtos;
using Hastane.Review.Api.Settings;
using MapsterMapper;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using ReviewEntity = Hastane.Review.Api.Entities.Review;

namespace Hastane.Review.Api.Services;

public class ReviewService : IReviewService
{
    private readonly IMongoCollection<ReviewEntity> _reviews;
    private readonly IMapper _mapper;

    // IMongoClient DI'dan gelir (Singleton): bağlantı havuzu uygulama boyunca tek ve paylaşılır.
    public ReviewService(IMapper mapper, IMongoClient client, IOptions<DatabaseSettings> options)
    {
        _mapper = mapper;
        IDatabaseSettings settings = options.Value;
        var database = client.GetDatabase(settings.DatabaseName);
        _reviews = database.GetCollection<ReviewEntity>(settings.ReviewCollectionName);
    }

    public async Task<List<ResultReviewDto>> GetAllAsync(CancellationToken ct)
    {
        var list = await _reviews.Find(_ => true).ToListAsync(ct);
        return _mapper.Map<List<ResultReviewDto>>(list);
    }

    public async Task<List<ResultReviewDto>> GetByDoctorIdAsync(string doctorId, CancellationToken ct)
    {
        var list = await _reviews.Find(r => r.DoctorId == doctorId)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return _mapper.Map<List<ResultReviewDto>>(list);
    }

    public async Task<ResultReviewDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        // Geçersiz biçimli id Mongo'da hata fırlatır; baştan "yok" sayıyoruz (500 yerine 404).
        if (!ObjectId.TryParse(id, out _)) return null;
        var entity = await _reviews.Find(r => r.Id == id).FirstOrDefaultAsync(ct);
        return entity is null ? null : _mapper.Map<ResultReviewDto>(entity);
    }

    public async Task<ResultReviewDto> CreateAsync(CreateReviewDto dto, CancellationToken ct)
    {
        var entity = _mapper.Map<ReviewEntity>(dto);
        // Zamanı sunucu damgalar: istemcinin saati yanlış ya da kötü niyetli olabilir.
        // Mongo tarihi milisaniye hassasiyetinde saklar; yanıt da aynı değeri göstersin diye baştan kırpıyoruz.
        var now = DateTime.UtcNow;
        entity.CreatedAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        await _reviews.InsertOneAsync(entity, cancellationToken: ct);
        return _mapper.Map<ResultReviewDto>(entity);
    }

    public async Task<bool> UpdateAsync(UpdateReviewDto dto, CancellationToken ct)
    {
        if (!ObjectId.TryParse(dto.Id, out _)) return false;
        // Belgeyi komple değiştirmek (Replace) yerine yalnız üç alanı Set ediyoruz:
        // DoctorId ve CreatedAt'e hiç dokunulmadığı için istemci ne gönderirse göndersin değişemez.
        var update = Builders<ReviewEntity>.Update
            .Set(r => r.ReviewerName, dto.ReviewerName)
            .Set(r => r.Rating, dto.Rating)
            .Set(r => r.Comment, dto.Comment);
        var result = await _reviews.UpdateOneAsync(r => r.Id == dto.Id, update, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return false;
        var result = await _reviews.DeleteOneAsync(r => r.Id == id, ct);
        return result.DeletedCount > 0;
    }

    public async Task<DoctorRatingSummaryDto> GetDoctorSummaryAsync(string doctorId, CancellationToken ct)
    {
        var groups = await AggregateByDoctorAsync(r => r.DoctorId == doctorId, ct);
        return ToSummary(doctorId, groups.FirstOrDefault());
    }

    public async Task<List<DoctorRatingSummaryDto>> GetDoctorSummariesAsync(IReadOnlyList<string> doctorIds, CancellationToken ct)
    {
        if (doctorIds.Count == 0) return [];

        // N+1 YOK: kimlik başına sorgu değil, tek $match ($in) + $group. Ağdan doktor başına tek satır gelir.
        var groups = (await AggregateByDoctorAsync(r => doctorIds.Contains(r.DoctorId), ct))
            .ToDictionary(g => g.DoctorId);

        // "İstenen her kimlik için kayıt" sözleşmesi: yorumsuz doktor aggregation'da çıkmaz, burada sıfırla doldurulur;
        // sıra da istenen sıradır (Mongo gruplarının sırası garanti değildir).
        return doctorIds.Select(id => ToSummary(id, groups.GetValueOrDefault(id))).ToList();
    }

    // Ortak aggregation: sayı, ortalama ve tavsiye sayısı (Rating >= 4) MongoDB'de hesaplanır.
    // Sum(x => x.Rating >= 4 ? 1 : 0) -> {$sum: {$cond: [{$gte: ["$Rating", 4]}, 1, 0]}}
    private async Task<List<RatingGroup>> AggregateByDoctorAsync(
        System.Linq.Expressions.Expression<Func<ReviewEntity, bool>> filter, CancellationToken ct)
        => await _reviews.Aggregate()
            .Match(filter)
            .Group(r => r.DoctorId, g => new RatingGroup(
                g.Key,
                g.Count(),
                g.Average(x => x.Rating),
                g.Sum(x => x.Rating >= 4 ? 1 : 0)))
            .ToListAsync(ct);

    private static DoctorRatingSummaryDto ToSummary(string doctorId, RatingGroup? group) => new()
    {
        DoctorId = doctorId,
        ReviewCount = group?.Count ?? 0,
        AverageRating = group is null ? 0 : Math.Round(group.Average, 1, MidpointRounding.AwayFromZero), // 4.25 -> 4.3 (varsayılan banker yuvarlaması 4.2 verirdi)
        // 5/8 = 62.5 -> 63 (banker yuvarlaması 62 verirdi). Yorum yoksa 0.
        RecommendationPercent = group is null ? 0
            : (int)Math.Round(100.0 * group.Recommended / group.Count, MidpointRounding.AwayFromZero),
    };

    private sealed record RatingGroup(string DoctorId, int Count, double Average, int Recommended);
}
