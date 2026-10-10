using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Hastane.Doctor.Api.Mapping;
using Microsoft.Extensions.Options;
using DoctorEntity = Hastane.Doctor.Api.Entities.Doctor;
using HttpMethod = Elastic.Transport.HttpMethod;

namespace Hastane.Doctor.Api.Search;

// Elasticsearch gerçekleştirmesi. Paketin ElasticsearchClient'ı bağlantıyı, zaman aşımını ve aktarımı yönetir;
// istek gövdeleri JSON olarak elle kurulur: analizör zinciri ve sorgu, tür güvenli ama ağır bir DSL yerine
// Kibana'da olduğu gibi okunabilsin diye.
public sealed class ElasticDoctorSearchIndex : IDoctorSearchIndex
{
    private const int MaxResultWindow = 10_000;

    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchOptions _options;
    private readonly ILogger<ElasticDoctorSearchIndex> _logger;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private volatile bool _indexReady;
    private long _unhealthyUntilTicks;

    public ElasticDoctorSearchIndex(IOptions<ElasticsearchOptions> options, ILogger<ElasticDoctorSearchIndex> logger)
    {
        _options = options.Value;
        _logger = logger;
        var settings = new ElasticsearchClientSettings(new Uri(_options.Url!))
            .RequestTimeout(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
        _client = new ElasticsearchClient(settings);
    }

    public bool Enabled => true;

    private string Index => _options.Index;

    // ---- Devre kesici: dizin düşünce her istek 2 sn beklemesin ----

    private bool CircuitOpen => Interlocked.Read(ref _unhealthyUntilTicks) > DateTime.UtcNow.Ticks;

    private void Trip(Exception? ex, string what)
    {
        Interlocked.Exchange(ref _unhealthyUntilTicks, DateTime.UtcNow.AddSeconds(_options.CircuitBreakSeconds).Ticks);
        _indexReady = false; // dizin kendi kendine yeniden kurulmuş olabilir; geri gelince tekrar doğrula
        _logger.LogWarning(ex, "Elasticsearch {What} başarısız; {Seconds} sn boyunca Mongo'ya düşülüyor.", what, _options.CircuitBreakSeconds);
    }

    private void Recovered() => Interlocked.Exchange(ref _unhealthyUntilTicks, 0);

    // ---- Alt düzey istek ----

    private async Task<(int Status, string Body)> SendAsync(HttpMethod method, string path, string? json, CancellationToken ct, string? contentType = null)
    {
        PostData? data = json is null ? null : PostData.String(json);
        IRequestConfiguration? local = contentType is null
            ? null
            : new RequestConfiguration { ContentType = contentType, Accept = "application/json" };
        var response = await _client.Transport.RequestAsync<StringResponse>(method, path, data!, local!, ct).ConfigureAwait(false);
        var status = response.ApiCallDetails.HttpStatusCode ?? 0;
        if (status == 0 && response.ApiCallDetails.OriginalException is { } ex) throw ex; // bağlantı/zaman aşımı
        return (status, response.Body ?? string.Empty);
    }

    private static string Esc(string s) => Uri.EscapeDataString(s);

    private string Refresh => _options.RefreshOnWrite ? "?refresh=true" : string.Empty;

    // ---- Dizin kurulumu ----

    private async Task EnsureIndexAsync(CancellationToken ct)
    {
        if (_indexReady) return;
        await _ensureLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_indexReady) return;
            var head = await SendAsync(HttpMethod.HEAD, "/" + Esc(Index), null, ct).ConfigureAwait(false);
            if (head.Status == 404)
            {
                var create = await SendAsync(HttpMethod.PUT, "/" + Esc(Index), IndexDefinition, ct).ConfigureAwait(false);
                // 400 resource_already_exists_exception: başka bir örnek aynı anda kurdu, sorun değil.
                if (create.Status is < 200 or >= 300 && !create.Body.Contains("resource_already_exists_exception", StringComparison.Ordinal))
                    throw new SearchIndexException($"Dizin oluşturulamadı ({create.Status}): {Trim(create.Body)}");
            }
            else if (head.Status is < 200 or >= 300)
            {
                throw new SearchIndexException($"Dizin denetlenemedi ({head.Status}).");
            }
            _indexReady = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    /// <summary>Açılışta çağrılır (best-effort).</summary>
    public async Task TryEnsureIndexAsync(CancellationToken ct)
    {
        try { await EnsureIndexAsync(ct).ConfigureAwait(false); Recovered(); }
        catch (Exception ex) when (!ct.IsCancellationRequested) { _logger.LogWarning(ex, "Elasticsearch açılışta hazırlanamadı; ilk başarılı istekte yeniden denenecek."); }
    }

    // Türkçe analiz:
    //  tr_fold = apostrophe + Türkçe küçük harf (I->ı, İ->i) + asciifolding  -> "Yilmaz" == "Yılmaz", "cocuk" == "Çocuk";
    //            ad/soyad/hizmet/unvan alanlarında (kök bulma YOK: bulanık eşleşme tahmin edilebilir kalsın).
    //  tr_text = tr_fold + Türkçe etkisiz sözcükler + Snowball Türkçe kök bulucu -> yalnız serbest metinde (biyografi).
    // Kök bulucu fold'DAN SONRA çalışır: sorgu da aynı zincirden geçtiği için "cocuk" ve "çocuk" aynı köke iner.
    // fullName / services: search_as_you_type = yazdıkça (önek) eşleşme için ._2gram/._3gram/._index_prefix alt alanları.
    private const string IndexDefinition = """
        {
          "settings": {
            "number_of_shards": 1,
            "number_of_replicas": 0,
            "analysis": {
              "filter": {
                "tr_lower": { "type": "lowercase", "language": "turkish" },
                "tr_stop": { "type": "stop", "stopwords": "_turkish_" },
                "tr_stem": { "type": "stemmer", "language": "turkish" }
              },
              "analyzer": {
                "tr_fold": { "type": "custom", "tokenizer": "standard", "filter": ["apostrophe", "tr_lower", "asciifolding"] },
                "tr_text": { "type": "custom", "tokenizer": "standard", "filter": ["apostrophe", "tr_lower", "tr_stop", "asciifolding", "tr_stem"] }
              }
            }
          },
          "mappings": {
            "dynamic": "strict",
            "properties": {
              "doctorId": { "type": "keyword" },
              "firstName": { "type": "text", "analyzer": "tr_fold", "fields": { "raw": { "type": "keyword", "ignore_above": 256 } } },
              "lastName": { "type": "text", "analyzer": "tr_fold", "fields": { "raw": { "type": "keyword", "ignore_above": 256 } } },
              "fullName": { "type": "search_as_you_type", "analyzer": "tr_fold" },
              "title": { "type": "text", "analyzer": "tr_fold" },
              "degrees": { "type": "text", "analyzer": "tr_fold" },
              "biography": { "type": "text", "analyzer": "tr_text" },
              "services": { "type": "search_as_you_type", "analyzer": "tr_fold" },
              "clinicNames": { "type": "text", "analyzer": "tr_fold" },
              "city": { "type": "keyword" },
              "gender": { "type": "keyword" },
              "branchId": { "type": "keyword" },
              "minFee": { "type": "double" },
              "maxFee": { "type": "double" },
              "scheduleDays": { "type": "keyword" },
              "locations": {
                "type": "nested",
                "properties": {
                  "city": { "type": "keyword" },
                  "fee": { "type": "double" },
                  "days": { "type": "keyword" }
                }
              },
              "indexedAt": { "type": "date" }
            }
          }
        }
        """;

    // ---- Belge ----

    private static JsonObject ToDocument(DoctorEntity d, DateTime indexedAtUtc)
    {
        var locations = d.ClinicLocations ?? [];
        var doc = new JsonObject
        {
            ["doctorId"] = d.Id,
            ["firstName"] = d.FirstName,
            ["lastName"] = d.LastName,
            ["fullName"] = $"{d.FirstName} {d.LastName}".Trim(),
            ["title"] = d.Title,
            ["degrees"] = d.Degrees,
            ["biography"] = d.Biography,
            ["services"] = new JsonArray([.. (d.Services ?? []).Select(s => (JsonNode?)JsonValue.Create(s))]),
            ["clinicNames"] = new JsonArray([.. locations.Select(l => (JsonNode?)JsonValue.Create(l.ClinicName))]),
            ["city"] = new JsonArray([.. locations.Select(l => l.City).Where(c => !string.IsNullOrEmpty(c)).Distinct().Select(c => (JsonNode?)JsonValue.Create(c))]),
            ["gender"] = d.Gender.ToString(),
            ["branchId"] = d.BranchId,
            ["scheduleDays"] = new JsonArray([.. locations.SelectMany(l => l.Schedule ?? []).Select(s => s.DayOfWeek.ToString()).Distinct().Select(s => (JsonNode?)JsonValue.Create(s))]),
            // Konumlar nested: şehir + ücret + gün AYNI konumda birlikte sağlanmalı (Mongo $elemMatch ile aynı anlam).
            // fee yalnız > 0 ise yazılır: "ücret filtresi verilince ücreti tanımlı olmalı" kuralı alanın varlığından gelir.
            ["locations"] = new JsonArray([.. locations.Select(l =>
            {
                var loc = new JsonObject
                {
                    ["city"] = l.City,
                    ["days"] = new JsonArray([.. (l.Schedule ?? []).Select(s => s.DayOfWeek.ToString()).Distinct().Select(s => (JsonNode?)JsonValue.Create(s))]),
                };
                if (l.HourlyFee is > 0) loc["fee"] = (double)l.HourlyFee.Value;
                return (JsonNode?)loc;
            })]),
            ["indexedAt"] = indexedAtUtc.ToString("o", CultureInfo.InvariantCulture),
        };
        // minFee/maxFee: kartta görünen tanım (DoctorDerived). Ücreti olmayan doktorda alan YOK -> sıralamada sona düşer.
        if (DoctorDerived.MinFee(locations) is { } min) doc["minFee"] = (double)min;
        if (DoctorDerived.MaxFee(locations) is { } max) doc["maxFee"] = (double)max;
        return doc;
    }

    // ---- Yazma ----

    public async Task<bool> UpsertAsync(DoctorEntity doctor, CancellationToken ct)
    {
        if (CircuitOpen || string.IsNullOrEmpty(doctor.Id)) return false;
        try
        {
            await EnsureIndexAsync(ct).ConfigureAwait(false);
            var r = await SendAsync(HttpMethod.PUT, $"/{Esc(Index)}/_doc/{Esc(doctor.Id)}{Refresh}",
                ToDocument(doctor, DateTime.UtcNow).ToJsonString(), ct).ConfigureAwait(false);
            if (r.Status is >= 200 and < 300) { Recovered(); return true; }
            _logger.LogWarning("Elasticsearch yazma {Status}: {Body}", r.Status, Trim(r.Body));
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Trip(ex, "yazma"); return false; }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        if (CircuitOpen) return false;
        try
        {
            await EnsureIndexAsync(ct).ConfigureAwait(false);
            var r = await SendAsync(HttpMethod.DELETE, $"/{Esc(Index)}/_doc/{Esc(id)}{Refresh}", null, ct).ConfigureAwait(false);
            if (r.Status is >= 200 and < 300 || r.Status == 404) { Recovered(); return true; }
            _logger.LogWarning("Elasticsearch silme {Status}: {Body}", r.Status, Trim(r.Body));
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Trip(ex, "silme"); return false; }
    }

    public async Task<int> BulkUpsertAsync(IReadOnlyList<DoctorEntity> doctors, CancellationToken ct)
    {
        if (doctors.Count == 0) return 0;
        try
        {
            await EnsureIndexAsync(ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var sb = new StringBuilder();
            foreach (var d in doctors.Where(d => !string.IsNullOrEmpty(d.Id)))
            {
                sb.Append(new JsonObject { ["index"] = new JsonObject { ["_index"] = Index, ["_id"] = d.Id } }.ToJsonString()).Append('\n');
                sb.Append(ToDocument(d, now).ToJsonString()).Append('\n');
            }
            var r = await SendAsync(HttpMethod.POST, "/_bulk?refresh=true", sb.ToString(), ct, "application/x-ndjson").ConfigureAwait(false);
            if (r.Status is < 200 or >= 300) throw new SearchIndexException($"Toplu yazma {r.Status}: {Trim(r.Body)}");
            using var parsed = JsonDocument.Parse(r.Body);
            if (parsed.RootElement.TryGetProperty("errors", out var errors) && errors.GetBoolean())
                throw new SearchIndexException($"Toplu yazmada hatalı öğeler var: {Trim(r.Body)}");
            Recovered();
            return doctors.Count(d => !string.IsNullOrEmpty(d.Id));
        }
        catch (SearchIndexException) { throw; }
        catch (Exception ex) when (!ct.IsCancellationRequested) { throw new SearchIndexException("Elasticsearch'e ulaşılamadı: " + ex.Message, ex); }
    }

    public async Task DeleteStaleAsync(DateTime rebuildStartedUtc, CancellationToken ct)
    {
        try
        {
            var body = new JsonObject
            {
                ["query"] = new JsonObject
                {
                    ["range"] = new JsonObject
                    {
                        ["indexedAt"] = new JsonObject { ["lt"] = rebuildStartedUtc.ToString("o", CultureInfo.InvariantCulture) },
                    },
                },
            };
            var r = await SendAsync(HttpMethod.POST, $"/{Esc(Index)}/_delete_by_query?refresh=true&conflicts=proceed", body.ToJsonString(), ct).ConfigureAwait(false);
            if (r.Status is < 200 or >= 300) throw new SearchIndexException($"Yetim temizliği {r.Status}: {Trim(r.Body)}");
        }
        catch (SearchIndexException) { throw; }
        catch (Exception ex) when (!ct.IsCancellationRequested) { throw new SearchIndexException("Elasticsearch'e ulaşılamadı: " + ex.Message, ex); }
    }

    public async Task<SearchIndexCounts> CountAsync(CancellationToken ct)
    {
        try
        {
            var r = await SendAsync(HttpMethod.GET, $"/{Esc(Index)}/_count", null, ct).ConfigureAwait(false);
            if (r.Status == 404) return new SearchIndexCounts(true, 0); // sunucu var, dizin henüz yok
            if (r.Status is < 200 or >= 300) return new SearchIndexCounts(false, null);
            using var doc = JsonDocument.Parse(r.Body);
            return new SearchIndexCounts(true, doc.RootElement.GetProperty("count").GetInt64());
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Elasticsearch sayımı başarısız.");
            return new SearchIndexCounts(false, null);
        }
    }

    // ---- Arama ----

    public async Task<EsSearchResult?> SearchAsync(DoctorSearchCriteria c, int skip, int take, CancellationToken ct)
    {
        if (CircuitOpen) return null;
        // ES'in sayfa penceresi 10 000: daha derini Mongo'ya bırak (hata yerine doğru sonuç).
        if ((long)skip + take > MaxResultWindow) return null;
        try
        {
            await EnsureIndexAsync(ct).ConfigureAwait(false);
            var body = BuildSearchBody(c, skip, take);
            var r = await SendAsync(HttpMethod.POST, $"/{Esc(Index)}/_search", body.ToJsonString(), ct).ConfigureAwait(false);
            if (r.Status is < 200 or >= 300)
            {
                _logger.LogWarning("Elasticsearch arama {Status}: {Body}", r.Status, Trim(r.Body));
                if (r.Status >= 500) Trip(null, "arama");
                return null;
            }
            using var doc = JsonDocument.Parse(r.Body);
            var hits = doc.RootElement.GetProperty("hits");
            var total = hits.GetProperty("total").GetProperty("value").GetInt64();
            var ids = hits.GetProperty("hits").EnumerateArray().Select(h => h.GetProperty("_id").GetString()!).ToList();
            Recovered();
            return new EsSearchResult(ids, total);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Trip(ex, "arama"); return null; }
    }

    internal static JsonObject BuildSearchBody(DoctorSearchCriteria c, int skip, int take)
    {
        var filters = new JsonArray();
        if (c.Gender is { } g) filters.Add(new JsonObject { ["term"] = new JsonObject { ["gender"] = g.ToString() } });
        if (c.BranchIds.Count > 0)
            filters.Add(new JsonObject { ["terms"] = new JsonObject { ["branchId"] = new JsonArray([.. c.BranchIds.Select(b => (JsonNode?)JsonValue.Create(b))]) } });

        if (c.HasLocationFilter)
        {
            var parts = new JsonArray();
            if (c.City is not null) parts.Add(new JsonObject { ["term"] = new JsonObject { ["locations.city"] = c.City } });
            if (c.MinFee is not null || c.MaxFee is not null)
            {
                var range = new JsonObject();
                if (c.MinFee is { } min) range["gte"] = (double)min;
                if (c.MaxFee is { } max) range["lte"] = (double)max;
                // fee yalnız > 0 ücretlerde var; range, alanı olmayan konumu zaten dışarıda bırakır.
                parts.Add(new JsonObject { ["range"] = new JsonObject { ["locations.fee"] = range } });
            }
            if (c.Day is { } day) parts.Add(new JsonObject { ["term"] = new JsonObject { ["locations.days"] = day.ToString() } });
            filters.Add(new JsonObject
            {
                ["nested"] = new JsonObject
                {
                    ["path"] = "locations",
                    ["query"] = new JsonObject { ["bool"] = new JsonObject { ["filter"] = parts } },
                },
            });
        }

        var bool_ = new JsonObject { ["filter"] = filters };
        if (!string.IsNullOrWhiteSpace(c.Text))
        {
            bool_["must"] = new JsonArray(TextQuery(c.Text!.Trim()));
        }

        // Sıralama: metin varken ve sıralama istenmediyse ALAKA (_score); aksi halde Mongo ile aynı kurallar.
        // Her sıralamanın sonunda doctorId (ObjectId sırası = Mongo _id sırası): sayfalar arasında kararlı.
        JsonArray sort;
        if (!c.SortGiven && !string.IsNullOrWhiteSpace(c.Text))
            sort = [new JsonObject { ["_score"] = "desc" }, new JsonObject { ["doctorId"] = "asc" }];
        else
            sort = c.SortKey switch
            {
                "feeasc" => [new JsonObject { ["minFee"] = new JsonObject { ["order"] = "asc", ["missing"] = "_last" } }, new JsonObject { ["lastName.raw"] = "asc" }, new JsonObject { ["doctorId"] = "asc" }],
                "feedesc" => [new JsonObject { ["maxFee"] = new JsonObject { ["order"] = "desc", ["missing"] = "_last" } }, new JsonObject { ["lastName.raw"] = "asc" }, new JsonObject { ["doctorId"] = "asc" }],
                _ => [new JsonObject { ["lastName.raw"] = "asc" }, new JsonObject { ["firstName.raw"] = "asc" }, new JsonObject { ["doctorId"] = "asc" }],
            };

        return new JsonObject
        {
            ["from"] = skip,
            ["size"] = take,
            ["track_total_hits"] = true,
            ["_source"] = false,
            ["query"] = new JsonObject { ["bool"] = bool_ },
            ["sort"] = sort,
        };
    }

    // Metin sorgusu iki yoldan biri tutsa yeter:
    //  1) multi_match + fuzziness AUTO: tam sözcük eşleşmesi ve yazım hatası toleransı ("kardiyolji" -> "Kardiyoloji");
    //     operator=and: yazılan sözcüklerin hepsi aynı alanda bulunmalı ("ali yilmaz" fullName'de).
    //  2) bool_prefix: son sözcük ÖNEK sayılır ("yilm" -> "Yılmaz"), yazdıkça arama.
    private static JsonObject TextQuery(string text) => new()
    {
        ["bool"] = new JsonObject
        {
            ["minimum_should_match"] = 1,
            ["should"] = new JsonArray(
                new JsonObject
                {
                    ["multi_match"] = new JsonObject
                    {
                        ["query"] = text,
                        ["type"] = "best_fields",
                        ["operator"] = "and",
                        ["fuzziness"] = "AUTO",
                        ["prefix_length"] = 1,
                        ["fields"] = new JsonArray("firstName^3", "lastName^3", "fullName^2", "title", "degrees^2", "services^2", "clinicNames", "biography"),
                    },
                },
                new JsonObject
                {
                    ["multi_match"] = new JsonObject
                    {
                        ["query"] = text,
                        ["type"] = "bool_prefix",
                        ["fields"] = new JsonArray("fullName", "fullName._2gram", "fullName._3gram", "services", "services._2gram", "services._3gram"),
                    },
                }),
        },
    };

    // ---- Öneri ----

    public async Task<IReadOnlyList<SuggestSource>?> SuggestAsync(string text, int take, CancellationToken ct)
    {
        if (CircuitOpen) return null;
        try
        {
            await EnsureIndexAsync(ct).ConfigureAwait(false);
            var body = new JsonObject
            {
                ["size"] = take,
                ["_source"] = new JsonArray("firstName", "lastName", "title", "services"),
                ["query"] = new JsonObject
                {
                    ["multi_match"] = new JsonObject
                    {
                        ["query"] = text,
                        ["type"] = "bool_prefix",
                        ["fields"] = new JsonArray("fullName", "fullName._2gram", "fullName._3gram", "services", "services._2gram", "services._3gram"),
                    },
                },
            };
            var r = await SendAsync(HttpMethod.POST, $"/{Esc(Index)}/_search", body.ToJsonString(), ct).ConfigureAwait(false);
            if (r.Status is < 200 or >= 300) { _logger.LogWarning("Elasticsearch öneri {Status}: {Body}", r.Status, Trim(r.Body)); return null; }
            using var doc = JsonDocument.Parse(r.Body);
            var list = new List<SuggestSource>();
            foreach (var h in doc.RootElement.GetProperty("hits").GetProperty("hits").EnumerateArray())
            {
                var src = h.GetProperty("_source");
                list.Add(new SuggestSource(
                    h.GetProperty("_id").GetString()!,
                    Str(src, "firstName") ?? string.Empty,
                    Str(src, "lastName") ?? string.Empty,
                    Str(src, "title"),
                    src.TryGetProperty("services", out var sv) && sv.ValueKind == JsonValueKind.Array
                        ? [.. sv.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x))!]
                        : []));
            }
            Recovered();
            return list;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Trip(ex, "öneri"); return null; }
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string Trim(string s) => s.Length <= 400 ? s : s[..400] + "...";
}
