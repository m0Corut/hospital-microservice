using System.Globalization;
using System.Text.RegularExpressions;
using Hastane.Doctor.Api.Dtos;
using Hastane.Doctor.Api.Entities;
using Hastane.Doctor.Api.Search;
using Hastane.Doctor.Api.Settings;
using Hastane.Doctor.Api.Validation;
using MapsterMapper;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using DoctorEntity = Hastane.Doctor.Api.Entities.Doctor;

namespace Hastane.Doctor.Api.Services;

public class DoctorService : IDoctorService
{
    private const int MaxBranchIds = 20;
    private const int MaxBatchIds = 100;

    private readonly IMongoCollection<DoctorEntity> _doctors;
    private const int MaxSuggestions = 8;
    private const int RebuildBatchSize = 200;

    private readonly IMapper _mapper;
    private readonly IDoctorSearchIndex _index;
    private readonly ElasticsearchOptions _esOptions;

    // IMongoClient DI'dan gelir (Singleton): bağlantı havuzu uygulama boyunca tek ve paylaşılır.
    public DoctorService(IMapper mapper, IMongoClient client, IOptions<DatabaseSettings> options,
        IDoctorSearchIndex index, IOptions<ElasticsearchOptions> esOptions)
    {
        _mapper = mapper;
        _index = index;
        _esOptions = esOptions.Value;
        IDatabaseSettings settings = options.Value;
        var database = client.GetDatabase(settings.DatabaseName);
        _doctors = database.GetCollection<DoctorEntity>(settings.DoctorCollectionName);
    }

    public async Task<List<ResultDoctorDto>> GetAllAsync(CancellationToken ct)
    {
        var list = await _doctors.Find(_ => true).ToListAsync(ct);
        return _mapper.Map<List<ResultDoctorDto>>(list);
    }

    public async Task<List<ResultDoctorDto>> GetByBranchIdAsync(string branchId, CancellationToken ct)
    {
        var list = await _doctors.Find(d => d.BranchId == branchId).ToListAsync(ct);
        return _mapper.Map<List<ResultDoctorDto>>(list);
    }

    public async Task<GetByIdDoctorDto?> GetByIdAsync(string id, CancellationToken ct)
    {
        // Geçersiz biçimli id Mongo'da hata fırlatır; baştan "yok" sayıyoruz (500 yerine 404).
        if (!ObjectId.TryParse(id, out _)) return null;
        var entity = await _doctors.Find(d => d.Id == id).FirstOrDefaultAsync(ct);
        return entity is null ? null : _mapper.Map<GetByIdDoctorDto>(entity);
    }

    public async Task<ResultDoctorDto> CreateAsync(CreateDoctorDto dto, CancellationToken ct)
    {
        DoctorInputValidator.ValidateOrThrow(dto);
        var entity = _mapper.Map<DoctorEntity>(dto);
        await _doctors.InsertOneAsync(entity, cancellationToken: ct);
        // Kaynak gerçek (Mongo) yazıldı; arama dizini best-effort: hata asıl işlemi bozmaz (tutarsızlık için rebuild ucu).
        await _index.UpsertAsync(entity, ct);
        return _mapper.Map<ResultDoctorDto>(entity);
    }

    public async Task<bool> UpdateAsync(UpdateDoctorDto dto, CancellationToken ct)
    {
        DoctorInputValidator.ValidateOrThrow(dto);
        if (!ObjectId.TryParse(dto.Id, out _)) return false;
        var entity = _mapper.Map<DoctorEntity>(dto);
        var result = await _doctors.ReplaceOneAsync(d => d.Id == dto.Id, entity, cancellationToken: ct);
        if (result.MatchedCount <= 0) return false;
        await _index.UpsertAsync(entity, ct);
        return true;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return false;
        var result = await _doctors.DeleteOneAsync(d => d.Id == id, ct);
        if (result.DeletedCount <= 0) return false;
        await _index.DeleteAsync(id, ct);
        return true;
    }

    // Arama/toplu getirmede yalnız kartın ihtiyacı olan alanlar okunur: Phone/Email/Biography
    // veritabanından hiç çekilmez (sızma riski sıfır, ağ trafiği küçük).
    private static readonly ProjectionDefinition<DoctorEntity> CardProjection =
        Builders<DoctorEntity>.Projection
            .Include(d => d.FirstName).Include(d => d.LastName).Include(d => d.Title).Include(d => d.Degrees)
            .Include(d => d.Gender).Include(d => d.BranchId).Include(d => d.ImageUrl)
            .Include(d => d.Services).Include(d => d.Gallery)
            .Include("ClinicLocations.City").Include("ClinicLocations.HourlyFee").Include("ClinicLocations.Currency");

    public async Task<PagedResult<DoctorSearchItemDto>> SearchAsync(DoctorSearchQuery query, CancellationToken ct)
        => (await SearchWithEngineAsync(query, ct)).Result;

    // Metin doluysa ve Elasticsearch açık/sağlıksa ARAMA DİZİNİ, değilse (boş metin, kapalı, hata, 2 sn zaman aşımı,
    // 10 000'den derin sayfa) mevcut Mongo araması. Doğrulama her iki yolda ÖNCE ve aynı kodla yapılır.
    public async Task<(PagedResult<DoctorSearchItemDto> Result, string Engine)> SearchWithEngineAsync(DoctorSearchQuery query, CancellationToken ct)
    {
        var filter = BuildSearchFilter(query, out var sort, out var criteria);

        if (_index.Enabled && !string.IsNullOrEmpty(criteria.Text))
        {
            var es = await _index.SearchAsync(criteria, SkipCount(query.Page, query.PageSize), query.PageSize, ct);
            if (es is not null)
            {
                // Dizin yalnız KİMLİKLERİ ve sırayı verir; belge Mongo'dan (kaynak gerçek) tek $in sorgusuyla gelir.
                var docs = await FetchInOrderAsync(es.Ids, ct);
                return (new PagedResult<DoctorSearchItemDto>
                {
                    Items = _mapper.Map<List<DoctorSearchItemDto>>(docs),
                    TotalCount = es.Total,
                    Page = query.Page,
                    PageSize = query.PageSize,
                }, "elasticsearch");
            }
        }

        // Sayfa: skip/limit MongoDB'de. TotalCount: AYNI filtreyle ayrı sayım; çünkü Find().Limit() yalnız
        // bu sayfanın satırlarını döndürür, "toplam kaç eşleşme var" bilgisini taşımaz.
        var skip = SkipCount(query.Page, query.PageSize);
        var sortKey = query.Sort?.Trim().ToLowerInvariant();
        var items = sortKey is "feeasc" or "feedesc"
            ? await SearchByFeeAsync(filter, sortKey == "feedesc", skip, query.PageSize, ct)
            : await _doctors.Find(filter).Project<DoctorEntity>(CardProjection).Sort(sort)
                .Skip(skip).Limit(query.PageSize).ToListAsync(ct);
        var total = await _doctors.CountDocumentsAsync(filter, cancellationToken: ct);

        return (new PagedResult<DoctorSearchItemDto>
        {
            Items = _mapper.Map<List<DoctorSearchItemDto>>(items),
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        }, "mongo");
    }

    // Kimlik listesindeki sırayı koruyarak tek sorguyla getirir (Mongo $in sırayı garanti etmez).
    // Dizinde olup Mongo'da artık olmayan kimlik (tutarsızlık) sessizce atlanır.
    private async Task<List<DoctorEntity>> FetchInOrderAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var found = await _doctors.Find(d => ids.Contains(d.Id!)).Project<DoctorEntity>(CardProjection).ToListAsync(ct);
        var byId = found.ToDictionary(d => d.Id!);
        return [.. ids.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    // Ücret sıralaması: yalnız HourlyFee > 0 olan konumlar "ücret" sayılır (DoctorDerived.MinFee ile aynı tanım).
    // Düz Sort() dizinin ham en küçük/en büyük öğesine bakar; 0 TL'lik konum yüzünden [0, 200] doktoru başa çıkardı
    // ve hiç ücreti olmayanlar (alan yok = en küçük) feeAsc'ta başı tutardı. Sort içinde dizi süzülemediği için
    // aggregation: önce pozitif ücretler ayıklanır (__fees), sonra "ücretsiz mi" (__noFee) ve min/max hesaplanır,
    // sıralama (__noFee, ücret, soyad, _id): ücretsiz doktorlar HER İKİ yönde de SONA gider.
    // __ ile başlayan yardımcı alanlar son $project'te düşer, DTO'ya sızmaz.
    private async Task<List<DoctorEntity>> SearchByFeeAsync(FilterDefinition<DoctorEntity> filter, bool descending,
        int skip, int limit, CancellationToken ct)
    {
        var feeKey = descending ? "__max" : "__min";
        var pipeline = _doctors.Aggregate()
            .Match(filter)
            .AppendStage<BsonDocument>(BsonDocument.Parse(
                "{ $addFields: { __fees: { $map: { input: { $filter: { input: { $ifNull: ['$ClinicLocations', []] }, as: 'l', cond: { $gt: ['$$l.HourlyFee', 0] } } }, as: 'l', in: '$$l.HourlyFee' } } } }"))
            .AppendStage<BsonDocument>(BsonDocument.Parse(
                "{ $addFields: { __noFee: { $eq: [{ $size: '$__fees' }, 0] }, __min: { $min: '$__fees' }, __max: { $max: '$__fees' } } }"))
            .AppendStage<BsonDocument>(new BsonDocument("$sort", new BsonDocument
            {
                { "__noFee", 1 }, { feeKey, descending ? -1 : 1 }, { "LastName", 1 }, { "_id", 1 },
            }))
            .AppendStage<BsonDocument>(new BsonDocument("$skip", skip))
            .AppendStage<BsonDocument>(new BsonDocument("$limit", limit))
            .AppendStage<DoctorEntity>(BsonDocument.Parse(
                "{ $project: { FirstName: 1, LastName: 1, Title: 1, Degrees: 1, Gender: 1, BranchId: 1, ImageUrl: 1, Services: 1, Gallery: 1, 'ClinicLocations.City': 1, 'ClinicLocations.HourlyFee': 1, 'ClinicLocations.Currency': 1 } }"));
        return await pipeline.ToListAsync(ct);
    }

    // Derin sayfa pahalıdır: MongoDB skip kadar belgeyi de tek tek sayıp atlar. Üst sınır (MaxPage) bu maliyeti
    // ve int taşmasını keser; hesap long + checked ile yapılır.
    private const int MaxPage = 10000;

    private static int SkipCount(int page, int pageSize) => checked((int)((long)(page - 1) * pageSize));

    private static FilterDefinition<DoctorEntity> BuildSearchFilter(DoctorSearchQuery q, out SortDefinition<DoctorEntity> sort, out DoctorSearchCriteria criteria)
    {
        DoctorGender? parsedGender = null;
        IReadOnlyList<string> parsedBranches = [];
        DayOfWeek? parsedDay = null;
        var errors = new Dictionary<string, string[]>();
        var f = Builders<DoctorEntity>.Filter;
        var lf = Builders<ClinicLocation>.Filter;
        var filters = new List<FilterDefinition<DoctorEntity>>();

        // Servis katmanı sınır savunması (DTO [Range] atlanırsa bile).
        if (q.Page < 1 || q.Page > MaxPage) errors["page"] = [$"page 1..{MaxPage} arasında olmalı."];
        if (q.PageSize < 1 || q.PageSize > 50) errors["pageSize"] = ["pageSize 1..50 arasında olmalı."];
        if (q.Text is { Length: > 100 }) errors["text"] = ["text en çok 100 karakter."];
        if (q.MinFee < 0) errors["minFee"] = ["minFee negatif olamaz."];
        if (q.MaxFee < 0) errors["maxFee"] = ["maxFee negatif olamaz."];
        if (q.MinFee > q.MaxFee) errors["minFeeMaxFee"] = ["minFee, maxFee'den büyük olamaz."];

        var text = q.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            // Regex.Escape: kullanıcı metni DÜZ METİN olarak aranır. Kaçırılmazsa ".*" her şeyi eşler, "(" geçersiz
            // desen olup hata fırlatır, kötü niyetli desen sunucuyu yorar (regex enjeksiyonu).
            // Regex araması indeksi KULLANAMAZ (baştan sona tarar), Türkçe ç/ı eşlemez, yazım hatası bilmez, alaka sıralamaz.
            // Metin aramasının asıl yolu artık Elasticsearch (SearchWithEngineAsync); bu, kapalı/hatalı durumdaki yedek.
            var rx = new BsonRegularExpression(Regex.Escape(text), "i");
            filters.Add(f.Or(
                f.Regex(nameof(DoctorEntity.FirstName), rx),
                f.Regex(nameof(DoctorEntity.LastName), rx),
                f.Regex(nameof(DoctorEntity.Degrees), rx),
                f.Regex(nameof(DoctorEntity.Services), rx),
                f.Regex("ClinicLocations.ClinicName", rx)));
        }

        if (!string.IsNullOrWhiteSpace(q.Gender))
        {
            // Sayısal metin ("1") kabul edilmez: yalnız adı.
            if (int.TryParse(q.Gender, out _) || !Enum.TryParse<DoctorGender>(q.Gender.Trim(), true, out var gender) || !Enum.IsDefined(gender))
                errors["gender"] = ["gender Male, Female veya Other olmalı."];
            else
            {
                parsedGender = gender;
                filters.Add(f.Eq(d => d.Gender, gender));
            }
        }

        if (!string.IsNullOrWhiteSpace(q.BranchIds))
        {
            var ids = q.BranchIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
            if (ids.Count > MaxBranchIds) errors["branchIds"] = [$"branchIds en çok {MaxBranchIds} öğe."];
            else if (ids.Count > 0)
            {
                parsedBranches = ids;
                filters.Add(f.In(d => d.BranchId, ids));
            }
        }

        // KONUM FİLTRELERİ (city, minFee/maxFee, date) TEK $elemMatch içinde birleşir: verilenlerin HEPSİNİ AYNI konum
        // sağlamalı. Ayrı ayrı koşul olsaydı her biri farklı konumla sağlanabilirdi (İzmir'de ucuz + Ankara'da pahalı
        // doktor, "city=Ankara&maxFee=500" aramasında yanlışlıkla eşleşirdi).
        var locationParts = new List<FilterDefinition<ClinicLocation>>();

        if (!string.IsNullOrWhiteSpace(q.City))
            locationParts.Add(lf.Eq(l => l.City, q.City.Trim()));

        // Ücret filtresi verilince konumun ücreti TANIMLI (> 0) olmalı: 0 ya da alan yok "ücretsiz/tanımsız" sayılır,
        // kartta görünen MinFee ile aynı tanım. Filtre verilmezse ücretsiz doktorlar listede kalır.
        if (q.MinFee is not null || q.MaxFee is not null)
            locationParts.Add(lf.Gt(l => l.HourlyFee, 0m));
        if (q.MinFee is { } min) locationParts.Add(lf.Gte(l => l.HourlyFee, min));
        if (q.MaxFee is { } max) locationParts.Add(lf.Lte(l => l.HourlyFee, max));

        if (!string.IsNullOrWhiteSpace(q.Date))
        {
            if (!DateOnly.TryParseExact(q.Date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                errors["date"] = ["date yyyy-MM-dd biçiminde geçerli bir tarih olmalı."];
            else
            {
                // Tarih -> haftanın günü; o konumun programında o gün varsa doktor orada çalışıyordur.
                parsedDay = date.DayOfWeek;
                var sf = Builders<ScheduleSlot>.Filter;
                locationParts.Add(lf.ElemMatch(l => l.Schedule, sf.Eq(s => s.DayOfWeek, date.DayOfWeek)));
            }
        }

        if (locationParts.Count > 0)
            filters.Add(f.ElemMatch(d => d.ClinicLocations, lf.And(locationParts)));

        var sortBuilder = Builders<DoctorEntity>.Sort;
        var sortKey = string.IsNullOrWhiteSpace(q.Sort) ? "name" : q.Sort.Trim();
        // Her sıralamanın sonunda Id: eşit değerlerde sıra kararlı olsun, yoksa sayfa kaydırınca kayıt tekrar eder/atlanır.
        sort = sortKey.ToLowerInvariant() switch
        {
            "name" => sortBuilder.Ascending(d => d.LastName).Ascending(d => d.FirstName).Ascending(d => d.Id),
            // Dizi alanında artan sıralama dizinin EN KÜÇÜK, azalan sıralama EN BÜYÜK öğesine bakar = MinFee / MaxFee.
            "feeasc" => sortBuilder.Ascending("ClinicLocations.HourlyFee").Ascending(d => d.LastName).Ascending(d => d.Id),
            "feedesc" => sortBuilder.Descending("ClinicLocations.HourlyFee").Ascending(d => d.LastName).Ascending(d => d.Id),
            _ => sortBuilder.Ascending(d => d.Id),
        };
        if (sortKey.ToLowerInvariant() is not ("name" or "feeasc" or "feedesc"))
            errors["sort"] = ["sort name, feeAsc veya feeDesc olmalı."];

        if (errors.Count > 0) throw new DoctorValidationException(errors);
        criteria = new DoctorSearchCriteria
        {
            Text = string.IsNullOrEmpty(text) ? null : text,
            Gender = parsedGender,
            BranchIds = parsedBranches,
            City = string.IsNullOrWhiteSpace(q.City) ? null : q.City.Trim(),
            MinFee = q.MinFee,
            MaxFee = q.MaxFee,
            Day = parsedDay,
            SortKey = sortKey.ToLowerInvariant(),
            SortGiven = !string.IsNullOrWhiteSpace(q.Sort),
        };
        return filters.Count == 0 ? f.Empty : f.And(filters);
    }

    public async Task<PublicDoctorDto?> GetPublicByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _)) return null;
        // Telefon ve e-posta veritabanından hiç çekilmez (projeksiyon) VE DTO'da alan yok: iki kat güvence.
        var entity = await _doctors.Find(d => d.Id == id)
            .Project<DoctorEntity>(Builders<DoctorEntity>.Projection.Exclude(d => d.Phone).Exclude(d => d.Email))
            .FirstOrDefaultAsync(ct);
        return entity is null ? null : _mapper.Map<PublicDoctorDto>(entity);
    }

    public async Task<List<DoctorSummaryDto>> GetBatchAsync(string? ids, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ids)) return [];
        var tokens = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        if (tokens.Count > MaxBatchIds)
            throw new DoctorValidationException("ids", $"En çok {MaxBatchIds} id gönderilebilir.");

        // Geçersiz ObjectId sessizce atılır: Mongo'ya bozuk id gitseydi tüm istek 500 olurdu.
        var valid = tokens.Where(t => ObjectId.TryParse(t, out _)).Select(t => ObjectId.Parse(t).ToString()).Distinct().ToList();
        if (valid.Count == 0) return [];

        // TEK sorgu ($in): 100 doktor için 100 ayrı GetById çağrısı (N+1) yerine tek gidiş-dönüş.
        var found = await _doctors.Find(f => valid.Contains(f.Id!)).Project<DoctorEntity>(CardProjection).ToListAsync(ct);

        // İstenen sırayı koru (Mongo $in sırayı garanti etmez).
        var order = valid.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        found = [.. found.OrderBy(d => order[d.Id!])];
        return _mapper.Map<List<DoctorSummaryDto>>(found);
    }

    // ---- Öneri (yazdıkça) ----

    public async Task<List<DoctorSuggestionDto>> SuggestAsync(string? q, CancellationToken ct)
    {
        var text = q?.Trim();
        if (string.IsNullOrEmpty(text)) return [];
        if (text.Length > 100) throw new DoctorValidationException("q", "q en çok 100 karakter.");

        // Dizin açıksa ona sor (Türkçe karakter duyarsız, önek eşleşmesi); kapalı/hatalıysa Mongo regex ile basit öneri.
        var viaEs = await _index.SuggestAsync(text, 20, ct);
        if (viaEs is not null)
            return BuildSuggestions(viaEs, c => TextFold.MatchesPrefixes(c, text));

        // Sözcük sözcük: "mehmet yil" iki sözcüğün HER biri ad/soyad/hizmetten birinde geçmeli (ad ve soyad ayrı alanlar).
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var f = Builders<DoctorEntity>.Filter;
        var docs = await _doctors.Find(f.And(words.Select(w =>
            {
                var rx = new BsonRegularExpression(Regex.Escape(w), "i");
                return f.Or(
                    f.Regex(nameof(DoctorEntity.FirstName), rx),
                    f.Regex(nameof(DoctorEntity.LastName), rx),
                    f.Regex(nameof(DoctorEntity.Services), rx));
            })))
            .Project<DoctorEntity>(Builders<DoctorEntity>.Projection
                .Include(d => d.FirstName).Include(d => d.LastName).Include(d => d.Title).Include(d => d.Services))
            .Sort(Builders<DoctorEntity>.Sort.Ascending(d => d.LastName).Ascending(d => d.FirstName).Ascending(d => d.Id))
            .Limit(20).ToListAsync(ct);
        var sources = docs.Select(d => new SuggestSource(d.Id!, d.FirstName, d.LastName, d.Title, d.Services ?? [])).ToList();
        return BuildSuggestions(sources, c => words.All(w => c.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    // Önce doktor adları, sonra hizmet adları; aynı metin bir kez; en çok 8.
    private static List<DoctorSuggestionDto> BuildSuggestions(IEnumerable<SuggestSource> sources, Func<string, bool> matches)
    {
        var sourceList = sources.ToList();
        var result = new List<DoctorSuggestionDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in sourceList)
        {
            var name = $"{s.FirstName} {s.LastName}".Trim();
            if (name.Length > 0 && matches(name) && seen.Add("d:" + TextFold.Fold(name)))
                result.Add(new DoctorSuggestionDto { Text = name, Type = "doctor", DoctorId = s.Id });
        }
        foreach (var s in sourceList)
            foreach (var service in s.Services)
                if (matches(service) && seen.Add("s:" + TextFold.Fold(service)))
                    result.Add(new DoctorSuggestionDto { Text = service, Type = "service" });

        return result.Count > MaxSuggestions ? result[..MaxSuggestions] : result;
    }

    // ---- Arama dizini yönetimi ----

    public async Task<int?> RebuildSearchIndexAsync(CancellationToken ct)
    {
        if (!_index.Enabled) return null;

        // Dizin önce SİLİNMEZ (silinse yeniden dolana dek aramalar boş dönerdi): her doktor yeniden yazılır (upsert),
        // sonra bu çalışmadan ESKİ damgalı belgeler (Mongo'da artık olmayan yetimler) silinir.
        var started = DateTime.UtcNow;
        var total = 0;
        using var cursor = await _doctors.Find(FilterDefinition<DoctorEntity>.Empty, new FindOptions { BatchSize = RebuildBatchSize })
            .Sort(Builders<DoctorEntity>.Sort.Ascending(d => d.Id)).ToCursorAsync(ct);
        while (await cursor.MoveNextAsync(ct))
        {
            var batch = cursor.Current.ToList();
            for (var i = 0; i < batch.Count; i += RebuildBatchSize)
                total += await _index.BulkUpsertAsync(batch.GetRange(i, Math.Min(RebuildBatchSize, batch.Count - i)), ct);
        }
        await _index.DeleteStaleAsync(started, ct);
        return total;
    }

    public async Task<SearchIndexStatusDto> GetSearchIndexStatusAsync(CancellationToken ct)
    {
        var mongo = await _doctors.CountDocumentsAsync(FilterDefinition<DoctorEntity>.Empty, cancellationToken: ct);
        if (!_index.Enabled)
            return new SearchIndexStatusDto { Enabled = false, MongoCount = mongo };

        var counts = await _index.CountAsync(ct);
        return new SearchIndexStatusDto
        {
            Enabled = true,
            Reachable = counts.Reachable,
            Index = _esOptions.Index,
            IndexedCount = counts.Documents,
            MongoCount = mongo,
            InSync = counts.Reachable && counts.Documents == mongo,
        };
    }
}