using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Chat;
using Hastane.WebUI.ViewModels.Public;

namespace Hastane.WebUI.Clients;

// Hasta-doktor mesajlaşması (Chat servisi). Admin hiçbir konuşmayı okuyamaz (servis 403 verir); WebUI de Admin'i kapıdan çevirir.
public interface IChatClient
{
    Task<ApiResult<List<ConversationApiModel>>> ListAsync(CancellationToken ct);

    Task<ApiResult<UnreadTotalApiModel>> GetUnreadAsync(ApiFailures handle, CancellationToken ct);

    // after verilirse yalnız o andan SONRAKİ mesajlar (yoklama); yoksa son `limit` mesaj. Konuşmada taraf değilsen 404.
    Task<ApiResult<List<MessageApiModel>>> GetMessagesAsync(string conversationId, DateTime? after, int limit, CancellationToken ct);

    Task<ApiResult<MessageApiModel>> SendAsync(string conversationId, string text, CancellationToken ct);

    Task<ApiResult> MarkReadAsync(string conversationId, ApiFailures handle, CancellationToken ct);

    Task<ApiResult<ConversationApiModel>> StartAsPatientAsync(string doctorId, ApiFailures handle, CancellationToken ct);

    Task<ApiResult<ConversationApiModel>> StartWithPatientAsync(string patientUserId, ApiFailures handle, CancellationToken ct);
}

public sealed class ChatClient(IGatewayClient gateway) : IChatClient
{
    public Task<ApiResult<List<ConversationApiModel>>> ListAsync(CancellationToken ct)
        => gateway.GetAsync<List<ConversationApiModel>>("/gateway/chat/conversations", ct: ct);

    public Task<ApiResult<UnreadTotalApiModel>> GetUnreadAsync(ApiFailures handle, CancellationToken ct)
        => gateway.GetAsync<UnreadTotalApiModel>("/gateway/chat/unread", handle, ct);

    public Task<ApiResult<List<MessageApiModel>>> GetMessagesAsync(string conversationId, DateTime? after, int limit, CancellationToken ct)
    {
        var path = $"/gateway/chat/conversations/{Uri.EscapeDataString(conversationId)}/messages?limit={limit}";
        // Saat dilimi (Z) zorunlu: sunucu DateTimeOffset bekler, belirsiz saat yanlış yorumlanır.
        if (after is { } a)
            path += "&after=" + Uri.EscapeDataString(ChatTime.Iso(a));
        return gateway.GetAsync<List<MessageApiModel>>(path, ct: ct);
    }

    public Task<ApiResult<MessageApiModel>> SendAsync(string conversationId, string text, CancellationToken ct)
        => gateway.PostAsync<MessageApiModel>($"/gateway/chat/conversations/{Uri.EscapeDataString(conversationId)}/messages",
            new SendMessageRequest(text), ct: ct);

    public Task<ApiResult> MarkReadAsync(string conversationId, ApiFailures handle, CancellationToken ct)
        => gateway.PostAsync($"/gateway/chat/conversations/{Uri.EscapeDataString(conversationId)}/read", null, handle, ct);

    public Task<ApiResult<ConversationApiModel>> StartAsPatientAsync(string doctorId, ApiFailures handle, CancellationToken ct)
        => gateway.PostAsync<ConversationApiModel>("/gateway/chat/conversations", new StartConversationRequest(doctorId), handle, ct);

    public Task<ApiResult<ConversationApiModel>> StartWithPatientAsync(string patientUserId, ApiFailures handle, CancellationToken ct)
        => gateway.PostAsync<ConversationApiModel>("/gateway/chat/conversations/with-patient",
            new StartConversationWithPatientRequest(patientUserId), handle, ct);
}

public static class ChatTime
{
    // UTC ISO-8601, milisaniye hassasiyeti (Mongo da milisaniye saklar), sonda Z.
    public static string Iso(DateTime value)
        => DisplayText.EnsureUtc(value).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
