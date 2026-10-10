using Hastane.Shared.Security;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Chat;
using Hastane.WebUI.ViewModels.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

// Hasta-doktor mesajlaşması. Admin giremez (403): mesajlar özel sağlık yazışmasıdır, servis de Admin'e 403 verir.
// Yetki etiketi action başına: sınıf düzeyi rol + action düzeyi rol VE'lenirdi (UI-KURALLARI §5), o yüzden sınıfta yok.
public class MessagesController(IChatClient chat) : AppController
{
    private const string PatientOrDoctor = HastaneRoles.Patient + "," + HastaneRoles.Doctor;
    private const int MessageLimit = 50;

    private bool IsPatientSide => User.IsInRole(HastaneRoles.Patient);

    [HttpGet("Messages"), Authorize(Roles = PatientOrDoctor)]
    public async Task<IActionResult> Index(CancellationToken ct)
        => View("Index", new MessagesViewModel { Conversations = await LoadConversationsAsync(ct) });

    [HttpGet("Messages/{id}"), Authorize(Roles = PatientOrDoctor)]
    public async Task<IActionResult> Conversation(string id, CancellationToken ct)
        => View("Index", await BuildAsync(id, new SendMessageForm(), markRead: true, ct));

    [HttpPost("Messages/{id}/Send"), Authorize(Roles = PatientOrDoctor)]
    public async Task<IActionResult> Send(string id, [Bind(Prefix = nameof(MessagesViewModel.Form))] SendMessageForm form, CancellationToken ct)
    {
        form.Text = (form.Text ?? string.Empty).Trim();
        if (ModelState.IsValid)
        {
            // Konuşmada taraf değilsen servis 404 verir: 404 sayfası (varlık sızdırılmaz).
            var result = await chat.SendAsync(id, form.Text, ct);
            if (result.IsSuccess) return RedirectToAction(nameof(Conversation), new { id });
            ModelState.AddApiProblem(result, "Mesaj gönderilemedi.");
        }

        // Hatalı gönderimde yazılan metin korunur; sayfa yeniden çizilir (okundu işareti zaten Conversation'da yapıldı).
        return View("Index", await BuildAsync(id, form, markRead: false, ct));
    }

    // Yoklama ucu (sayfadaki küçük JS her 5 sn çağırır): ?after=<ISO> sonrası yeni mesajlar, JSON. JS yoksa sayfa yenilemeyle çalışır.
    // Konuşmada taraf değilsen gateway 404 verir, bu uç da 404 döner.
    [HttpGet("Messages/{id}/Since"), Authorize(Roles = PatientOrDoctor)]
    public async Task<IActionResult> Since(string id, string? after, CancellationToken ct)
    {
        if (!DateTimeOffset.TryParse(after, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            return BadRequest(new { error = "after geçerli bir ISO tarih olmalı." });

        var result = await chat.GetMessagesAsync(id, parsed.UtcDateTime, MessageLimit, ct);
        var rows = (result.Value ?? []).Select(m => MessagesViewModel.ToRow(m, IsPatientSide)).ToList();

        // Karşı taraftan yeni mesaj geldiyse ve sayfa açıksa okundu say (rozet geriye kalmasın).
        if (rows.Any(r => !r.Mine))
            await chat.MarkReadAsync(id, ApiFailures.All, ct);

        // Metin JSON'da ham gider (System.Text.Json "<" gibi karakterleri < yapar); JS onu textContent ile yazar, innerHTML ile değil.
        return Json(new
        {
            messages = rows.Select(r => new { id = r.Id, mine = r.Mine, sender = r.SenderName, text = r.Text, time = r.TimeText, sentAt = ChatTime.Iso(r.SentAt) }),
        });
    }

    // Hasta: randevusu olan doktora yaz. Randevu yoksa servis 403 "Yalnız randevunuz olan doktora yazabilirsiniz." der; aynen gösterilir.
    [HttpPost("Messages/WithDoctor/{doctorId}"), Authorize(Roles = HastaneRoles.Patient)]
    public async Task<IActionResult> WithDoctor(string doctorId, string? returnUrl, CancellationToken ct)
    {
        var result = await chat.StartAsPatientAsync(doctorId, ApiFailures.Forbidden | ApiFailures.NotFound | ApiFailures.Unavailable, ct);
        return Started(result, returnUrl, "Yalnız randevunuz olan doktora yazabilirsiniz.");
    }

    // Doktor: randevusu olan hastaya yaz. Hasta kimliği rotadan (Hastalarım listesinden) gelir; doktor kimliği token'dan.
    [HttpPost("Messages/WithPatient/{patientUserId}"), Authorize(Roles = HastaneRoles.Doctor)]
    public async Task<IActionResult> WithPatient(string patientUserId, string? returnUrl, CancellationToken ct)
    {
        var result = await chat.StartWithPatientAsync(patientUserId, ApiFailures.Forbidden | ApiFailures.NotFound | ApiFailures.Unavailable, ct);
        return Started(result, returnUrl, "Yalnız randevusu olan hastaya yazabilirsiniz.");
    }

    private IActionResult Started(ApiResult<ConversationApiModel> result, string? returnUrl, string forbiddenFallback)
    {
        if (result.TryGetValue(out var conversation))
            return RedirectToAction(nameof(Conversation), new { id = conversation.Id });

        SetWarning(result.Kind switch
        {
            ApiResultKind.Forbidden => result.Problem?.Detail ?? forbiddenFallback,
            ApiResultKind.NotFound => result.Problem?.Title ?? "Kullanıcı bulunamadı.",
            ApiResultKind.Unavailable => "Mesajlaşma şu an kullanılamıyor, lütfen tekrar deneyin.",
            _ => result.Problem?.Detail ?? "Konuşma başlatılamadı.",
        });
        return string.IsNullOrEmpty(returnUrl) ? RedirectToAction(nameof(Index)) : RedirectToLocal(returnUrl);
    }

    private async Task<List<ConversationRowViewModel>> LoadConversationsAsync(CancellationToken ct)
    {
        var result = await chat.ListAsync(ct);
        return (result.Value ?? [])
            .OrderByDescending(c => c.LastMessageAt)
            .Select(c => new ConversationRowViewModel(
                c.Id, IsPatientSide ? c.DoctorName : c.PatientName, c.LastMessagePreview, c.LastMessageAt, c.UnreadCount))
            .ToList();
    }

    private async Task<MessagesViewModel> BuildAsync(string id, SendMessageForm form, bool markRead, CancellationToken ct)
    {
        var conversations = await LoadConversationsAsync(ct);
        var selected = conversations.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        // Listede olmayan konuşma = taraf değilsin (ya da yok): 404 sayfası, mesajlara hiç gidilmez.
        if (selected is null) throw new GatewayException(ApiResultKind.NotFound, StatusCodes.Status404NotFound, "Konuşma bulunamadı.", null);

        var messages = await chat.GetMessagesAsync(id, null, MessageLimit, ct);
        if (markRead)
        {
            await chat.MarkReadAsync(id, ApiFailures.All, ct);
            selected = selected with { Unread = 0 };
            conversations = conversations.Select(c => c.Id == id ? selected : c).ToList();
        }

        return new MessagesViewModel
        {
            Conversations = conversations,
            Selected = selected,
            Messages = (messages.Value ?? []).Select(m => MessagesViewModel.ToRow(m, IsPatientSide)).ToList(),
            Form = form,
        };
    }
}
