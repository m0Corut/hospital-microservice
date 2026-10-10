using Hastane.Chat.Api.Dtos;
using Hastane.Chat.Api.Entities;
using Hastane.Chat.Api.Integration;
using Hastane.Chat.Api.Settings;
using Hastane.Shared.Security;
using MapsterMapper;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Hastane.Chat.Api.Services;

public class ChatService : IChatService
{
    private const string FallbackPatientName = "Hasta";

    private readonly IMongoCollection<Conversation> _conversations;
    private readonly IMongoCollection<ChatMessage> _messages;
    private readonly IMapper _mapper;
    private readonly IAppointmentLookup _appointments;
    private readonly IDoctorLookup _doctors;
    private readonly IUserLookup _users;

    public ChatService(IMapper mapper, IMongoClient client, IOptions<DatabaseSettings> options,
        IAppointmentLookup appointments, IDoctorLookup doctors, IUserLookup users)
    {
        _mapper = mapper;
        _appointments = appointments;
        _doctors = doctors;
        _users = users;
        var settings = options.Value;
        var database = client.GetDatabase(settings.DatabaseName);
        _conversations = database.GetCollection<Conversation>(settings.ConversationCollectionName);
        _messages = database.GetCollection<ChatMessage>(settings.MessageCollectionName);
    }

    // ---- konuşma açma ----

    public async Task<StartResult> StartAsPatientAsync(ChatCaller caller, string doctorId, CancellationToken ct)
    {
        var existing = await _conversations.Find(c => c.PatientUserId == caller.UserId && c.DoctorId == doctorId).FirstOrDefaultAsync(ct);
        if (existing is not null) return new StartResult(StartStatus.Existing, ToDto(existing, caller));

        // Hastanın bu doktorla randevusu var mı? Hastanın token'ı iletilir; Appointment listeyi zaten ona göre süzer.
        var appointments = await _appointments.ListAsync(doctorId, caller.UserId, ct);
        if (appointments.Outcome == LookupOutcome.Unavailable) return new StartResult(StartStatus.Unavailable);
        if (appointments.Outcome != LookupOutcome.Found || appointments.Value is not { Count: > 0 })
            return new StartResult(StartStatus.NoAppointment);

        var doctor = await _doctors.GetPublicAsync(doctorId, ct);
        if (doctor.Outcome == LookupOutcome.NotFound) return new StartResult(StartStatus.DoctorNotFound);
        if (doctor.Outcome != LookupOutcome.Found || doctor.Value is null) return new StartResult(StartStatus.Unavailable);

        var patientName = string.IsNullOrWhiteSpace(caller.DisplayName) ? FallbackPatientName : caller.DisplayName;
        return await InsertAsync(caller.UserId, patientName, doctorId, DoctorDisplayName(doctor.Value), caller, ct);
    }

    public async Task<StartResult> StartAsDoctorAsync(ChatCaller caller, string patientUserId, CancellationToken ct)
    {
        var doctorId = caller.DoctorId!; // controller doctor_id'siz çağıranı 403 ile ayıklar
        var existing = await _conversations.Find(c => c.PatientUserId == patientUserId && c.DoctorId == doctorId).FirstOrDefaultAsync(ct);
        if (existing is not null) return new StartResult(StartStatus.Existing, ToDto(existing, caller));

        var appointments = await _appointments.ListAsync(doctorId, patientUserId, ct);
        if (appointments.Outcome == LookupOutcome.Unavailable) return new StartResult(StartStatus.Unavailable);
        if (appointments.Outcome != LookupOutcome.Found || appointments.Value is not { Count: > 0 })
            return new StartResult(StartStatus.NoAppointment);

        var doctor = await _doctors.GetPublicAsync(doctorId, ct);
        if (doctor.Outcome != LookupOutcome.Found || doctor.Value is null) return new StartResult(StartStatus.Unavailable);

        // Hasta adı Identity'den (doktor token'ı ile). Ulaşılamazsa ya da bulunamazsa konuşma yine açılır: ad "Hasta".
        var patientName = FallbackPatientName;
        var users = await _users.GetByIdsAsync([patientUserId], ct);
        var user = users.Value?.FirstOrDefault(u => string.Equals(u.Id, patientUserId, StringComparison.OrdinalIgnoreCase));
        if (users.Outcome == LookupOutcome.Found && user is not null)
        {
            var full = $"{user.FirstName} {user.LastName}".Trim();
            if (full.Length > 0) patientName = full;
        }

        return await InsertAsync(patientUserId, patientName, doctorId, DoctorDisplayName(doctor.Value), caller, ct);
    }

    private async Task<StartResult> InsertAsync(string patientUserId, string patientName, string doctorId, string doctorName,
        ChatCaller caller, CancellationToken ct)
    {
        var now = NowMs();
        var entity = new Conversation
        {
            PatientUserId = patientUserId,
            PatientName = patientName,
            DoctorId = doctorId,
            DoctorName = doctorName,
            CreatedAt = now,
            LastMessageAt = now,
        };
        try
        {
            await _conversations.InsertOneAsync(entity, cancellationToken: ct);
            return new StartResult(StartStatus.Created, ToDto(entity, caller));
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Yarış: aynı çift için başka bir istek bizden önce açtı. Tekil indeks korudu; var olanı döndür.
            var existing = await _conversations.Find(c => c.PatientUserId == patientUserId && c.DoctorId == doctorId).FirstAsync(ct);
            return new StartResult(StartStatus.Existing, ToDto(existing, caller));
        }
    }

    private static string DoctorDisplayName(PublicDoctorInfo d)
        => string.Join(' ', new[] { d.Title, d.FirstName, d.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));

    // ---- liste / okunmamış ----

    public async Task<List<ConversationDto>> ListAsync(ChatCaller caller, CancellationToken ct)
    {
        var list = await _conversations.Find(AccessFilter(caller))
            .SortByDescending(c => c.LastMessageAt)
            .Limit(200)
            .ToListAsync(ct);
        return list.Select(c => ToDto(c, caller)).ToList();
    }

    public async Task<int> GetUnreadTotalAsync(ChatCaller caller, CancellationToken ct)
    {
        var rows = await _conversations.Find(AccessFilter(caller))
            .Project(c => new { c.DoctorId, c.PatientUnread, c.DoctorUnread })
            .ToListAsync(ct);
        return rows.Sum(r => IsDoctorSide(r.DoctorId, caller) ? r.DoctorUnread : r.PatientUnread);
    }

    // ---- mesajlar ----

    public async Task<List<MessageDto>?> GetMessagesAsync(string conversationId, ChatCaller caller, DateTime? after, int limit, CancellationToken ct)
    {
        if (await FindAccessibleAsync(conversationId, caller, ct) is null) return null;

        var byConversation = Builders<ChatMessage>.Filter.Eq(m => m.ConversationId, conversationId);
        List<ChatMessage> items;
        if (after is null)
        {
            // Açılış: SON `limit` mesaj (yeniden eskiye çekilir), gösterim için eskiden yeniye çevrilir.
            items = await _messages.Find(byConversation).SortByDescending(m => m.SentAt).Limit(limit).ToListAsync(ct);
            items.Reverse();
        }
        else
        {
            // Yoklama: yalnız `after`dan SONRAKİLER (kesin büyük), eskiden yeniye. (ConversationId, SentAt) indeksinden aralık taraması.
            var filter = Builders<ChatMessage>.Filter.And(byConversation, Builders<ChatMessage>.Filter.Gt(m => m.SentAt, after.Value));
            items = await _messages.Find(filter).SortBy(m => m.SentAt).Limit(limit).ToListAsync(ct);
        }
        return items.Select(m => _mapper.Map<MessageDto>(m)).ToList();
    }

    public async Task<MessageDto?> SendAsync(string conversationId, ChatCaller caller, string text, CancellationToken ct)
    {
        var conversation = await FindAccessibleAsync(conversationId, caller, ct);
        if (conversation is null) return null;

        var isDoctor = IsDoctorSide(conversation.DoctorId, caller);
        var now = NowMs();
        var message = new ChatMessage
        {
            ConversationId = conversationId,
            SenderRole = isDoctor ? HastaneRoles.Doctor : HastaneRoles.Patient,
            SenderId = isDoctor ? conversation.DoctorId : conversation.PatientUserId,
            // Ad sunucu tarafında bilinen anlık görüntüden: istemci/token adı mesaj adını belirlemez.
            SenderName = isDoctor ? conversation.DoctorName : conversation.PatientName,
            Text = text,
            SentAt = now,
        };
        await _messages.InsertOneAsync(message, cancellationToken: ct);

        // Karşı tarafın sayacı tek atomik güncellemede $inc: iki mesaj aynı anda gelirse biri kaybolmaz.
        // db.conversations.updateOne({_id}, {$set:{LastMessageAt, LastMessagePreview}, $inc:{DoctorUnread:1}})
        var update = Builders<Conversation>.Update.Combine(
            Builders<Conversation>.Update.Set(c => c.LastMessageAt, now),
            Builders<Conversation>.Update.Set(c => c.LastMessagePreview, MessagePreview.Create(text)),
            isDoctor
                ? Builders<Conversation>.Update.Inc(c => c.PatientUnread, 1)
                : Builders<Conversation>.Update.Inc(c => c.DoctorUnread, 1));
        await _conversations.UpdateOneAsync(c => c.Id == conversationId, update, cancellationToken: ct);

        return _mapper.Map<MessageDto>(message);
    }

    public async Task<bool> MarkReadAsync(string conversationId, ChatCaller caller, CancellationToken ct)
    {
        var conversation = await FindAccessibleAsync(conversationId, caller, ct);
        if (conversation is null) return false;

        var reset = IsDoctorSide(conversation.DoctorId, caller)
            ? Builders<Conversation>.Update.Set(c => c.DoctorUnread, 0)
            : Builders<Conversation>.Update.Set(c => c.PatientUnread, 0);
        await _conversations.UpdateOneAsync(c => c.Id == conversationId, reset, cancellationToken: ct);
        return true;
    }

    // ---- yardımcılar ----

    // Erişim süzgeci: yalnız taraflar. Hasta tarafı sub ile, doktor tarafı doctor_id ile eşleşir. Hiçbiri yoksa hiçbir şey eşleşmez.
    private static FilterDefinition<Conversation> AccessFilter(ChatCaller caller)
    {
        var f = Builders<Conversation>.Filter;
        var parts = new List<FilterDefinition<Conversation>>();
        if (caller.IsPatient && !string.IsNullOrEmpty(caller.UserId)) parts.Add(f.Eq(c => c.PatientUserId, caller.UserId));
        if (caller.IsDoctor && !string.IsNullOrEmpty(caller.DoctorId)) parts.Add(f.Eq(c => c.DoctorId, caller.DoctorId));
        return parts.Count == 0 ? f.Where(_ => false) : f.Or(parts);
    }

    private async Task<Conversation?> FindAccessibleAsync(string id, ChatCaller caller, CancellationToken ct)
    {
        // Geçersiz biçimli id Mongo'da hata fırlatır; baştan "yok" sayıyoruz (500 yerine 404).
        if (!ObjectId.TryParse(id, out _)) return null;
        var filter = Builders<Conversation>.Filter.And(Builders<Conversation>.Filter.Eq(c => c.Id, id), AccessFilter(caller));
        return await _conversations.Find(filter).FirstOrDefaultAsync(ct);
    }

    // Erişim süzgecinden geçmiş bir konuşmada: doctor_id eşleşiyorsa doktor tarafı, değilse hasta tarafı.
    private static bool IsDoctorSide(string conversationDoctorId, ChatCaller caller)
        => caller.IsDoctor && !string.IsNullOrEmpty(caller.DoctorId) && caller.DoctorId == conversationDoctorId;

    private ConversationDto ToDto(Conversation entity, ChatCaller caller)
    {
        var dto = _mapper.Map<ConversationDto>(entity);
        dto.UnreadCount = IsDoctorSide(entity.DoctorId, caller) ? entity.DoctorUnread : entity.PatientUnread;
        return dto;
    }

    // Mongo tarihi milisaniye hassasiyetinde saklar; yanıt da aynı değeri göstersin diye baştan kırpıyoruz.
    private static DateTime NowMs()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }
}
