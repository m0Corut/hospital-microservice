using Hastane.Notification.Api.Dtos;
using Hastane.Notification.Api.Entities;
using Hastane.Notification.Api.Settings;
using MapsterMapper;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Hastane.Notification.Api.Services;

public class NotificationService : INotificationService
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;
    private const int MaxErrorLength = 300;

    private readonly IMongoCollection<AppNotification> _notifications;
    private readonly IMongoCollection<EmailLog> _emailLogs;
    private readonly IMapper _mapper;
    private readonly IEmailSender _sender;
    private readonly ILogger<NotificationService> _logger;

    // IMongoClient DI'dan gelir (Singleton): bağlantı havuzu uygulama boyunca tek.
    public NotificationService(
        IMapper mapper, IMongoClient client, IOptions<DatabaseSettings> options,
        IEmailSender sender, ILogger<NotificationService> logger)
    {
        _mapper = mapper;
        _sender = sender;
        _logger = logger;
        var settings = options.Value;
        var database = client.GetDatabase(settings.DatabaseName);
        _notifications = database.GetCollection<AppNotification>(settings.NotificationCollectionName);
        _emailLogs = database.GetCollection<EmailLog>(settings.EmailLogCollectionName);
    }

    public async Task<ResultNotificationDto> CreateAsync(CreateNotificationDto dto, CancellationToken ct)
    {
        var entity = _mapper.Map<AppNotification>(dto);
        // Mongo tarihi milisaniye hassasiyetinde saklar; yanıt da aynı değeri göstersin diye baştan kırpılır.
        var now = DateTime.UtcNow;
        entity.CreatedAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        await _notifications.InsertOneAsync(entity, cancellationToken: ct);
        return _mapper.Map<ResultNotificationDto>(entity);
    }

    public async Task<bool> CreateFromEventAsync(CreateNotificationDto dto, string sourceKey, CancellationToken ct)
    {
        var entity = _mapper.Map<AppNotification>(dto);
        entity.SourceKey = sourceKey;
        var now = DateTime.UtcNow;
        entity.CreatedAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        try
        {
            await _notifications.InsertOneAsync(entity, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Aynı olay ikinci kez teslim edildi: bildirim zaten var, hata değil.
            _logger.LogInformation("Olay bildirimi zaten işlenmişti ({SourceKey}); atlandı.", sourceKey);
            return false;
        }
    }

    // Alıcı = token sub (hasta) YA DA doctor_id (doktor): hangisi doluysa o kimliklere adreslenmiş bildirimler.
    private static FilterDefinition<AppNotification>? RecipientFilter(string? userId, string? doctorId)
    {
        var b = Builders<AppNotification>.Filter;
        var parts = new List<FilterDefinition<AppNotification>>();
        if (!string.IsNullOrEmpty(userId)) parts.Add(b.Eq(n => n.UserId, userId));
        if (!string.IsNullOrEmpty(doctorId)) parts.Add(b.Eq(n => n.DoctorId, doctorId));
        return parts.Count == 0 ? null : b.Or(parts);
    }

    public async Task<NotificationPageDto> GetMineAsync(
        string? userId, string? doctorId, bool unreadOnly, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var result = new NotificationPageDto { Page = page, PageSize = pageSize };

        var recipient = RecipientFilter(userId, doctorId);
        if (recipient is null) return result;

        var b = Builders<AppNotification>.Filter;
        var unread = b.Eq(n => n.ReadAt, null);
        var listFilter = unreadOnly ? b.And(recipient, unread) : recipient;

        result.TotalCount = await _notifications.CountDocumentsAsync(listFilter, cancellationToken: ct);
        result.UnreadCount = await _notifications.CountDocumentsAsync(b.And(recipient, unread), cancellationToken: ct);
        var items = await _notifications.Find(listFilter)
            .SortByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Limit(pageSize)
            .ToListAsync(ct);
        result.Items = _mapper.Map<List<ResultNotificationDto>>(items);
        return result;
    }

    public async Task<bool> MarkReadAsync(string id, string? userId, string? doctorId, CancellationToken ct)
    {
        // Geçersiz biçimli id Mongo'da hata fırlatır; baştan "yok" (404).
        if (!ObjectId.TryParse(id, out _)) return false;
        var recipient = RecipientFilter(userId, doctorId);
        if (recipient is null) return false;

        var b = Builders<AppNotification>.Filter;
        var mine = b.And(b.Eq(n => n.Id, id), recipient);
        // Zaten okunmuşsa ReadAt ilk okuma zamanında kalır; yine de 204 (idempotent).
        var update = await _notifications.UpdateOneAsync(
            b.And(mine, b.Eq(n => n.ReadAt, null)),
            Builders<AppNotification>.Update.Set(n => n.ReadAt, DateTime.UtcNow),
            cancellationToken: ct);
        if (update.MatchedCount > 0) return true;
        return await _notifications.CountDocumentsAsync(mine, cancellationToken: ct) > 0;
    }

    public async Task MarkAllReadAsync(string? userId, string? doctorId, CancellationToken ct)
    {
        var recipient = RecipientFilter(userId, doctorId);
        if (recipient is null) return;
        var b = Builders<AppNotification>.Filter;
        await _notifications.UpdateManyAsync(
            b.And(recipient, b.Eq(n => n.ReadAt, null)),
            Builders<AppNotification>.Update.Set(n => n.ReadAt, DateTime.UtcNow),
            cancellationToken: ct);
    }

    public async Task<bool> SendEmailAsync(SendEmailDto dto, CancellationToken ct)
    {
        var log = new EmailLog { To = dto.To, Subject = dto.Subject, SentAt = DateTime.UtcNow };
        try
        {
            await _sender.SendAsync(new EmailMessage(dto.To, dto.Subject, dto.TextBody, dto.HtmlBody), ct);
            log.Success = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // çağıran vazgeçti: bu bir SMTP hatası değil
        }
        catch (Exception ex)
        {
            log.Success = false;
            var error = $"{ex.GetType().Name}: {ex.Message}";
            log.Error = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
            _logger.LogWarning("E-posta gönderilemedi: {ExceptionType}", ex.GetType().Name);
        }

        // Günlük istemci bağlantıyı kapatsa da yazılsın; gövde günlüğe GİRMEZ (şifre sıfırlama bağlantısı olabilir).
        await _emailLogs.InsertOneAsync(log, cancellationToken: CancellationToken.None);
        return log.Success;
    }
}
