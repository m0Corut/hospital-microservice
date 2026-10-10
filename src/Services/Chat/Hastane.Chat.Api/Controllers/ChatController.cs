using Hastane.Chat.Api.Dtos;
using Hastane.Chat.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Chat.Api.Controllers;

// Sınıf düzeyinde yetki YOK, bilerek: her eylem kendi etiketini AÇIKÇA taşır; unutulan uç AuthorizationCoverage'ı kırar.
//
// Admin KARARI: Admin hiçbir konuşmayı okuyamaz ve yazamaz (her uç Patient/Doctor rolüne kapalı, Admin 403 alır).
// Gerekçe: mesajlar hasta-doktor arasındaki özel sağlık yazışmasıdır; yönetim yetkisi (kullanıcı/rol yönetimi) içerik
// okuma yetkisi doğurmaz. Moderasyon gerekirse ayrı, denetim kaydı tutan bir uç bilinçli olarak eklenmeli.
//
// Mesaj metni HAM saklanır ve ham döner; HTML kodlama (XSS) arayüzün işidir (Review yorumlarıyla aynı karar).
[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private const string PatientOrDoctor = HastaneRoles.Patient + "," + HastaneRoles.Doctor;
    private const int MaxTextLength = 2000;
    private const int DefaultLimit = 50;
    private const int MaxLimit = 100;

    private readonly IChatService _service;
    private readonly ICurrentUser _currentUser;

    public ChatController(IChatService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    // ---- konuşma açma ----

    // Hasta başlatır: bu doktorla en az bir randevusu olmalı. Var olan konuşma 200, yeni 201.
    [Authorize(Policy = HastanePolicies.PatientOnly)]
    [HttpPost("conversations")]
    public async Task<ActionResult<ConversationDto>> Start(StartConversationDto dto, CancellationToken ct)
    {
        var caller = Caller();
        if (caller is null) return Unauthorized();
        var doctorId = dto.DoctorId.Trim();
        if (doctorId.Length == 0) return ValidationFailure(nameof(dto.DoctorId), "doctorId zorunludur.");

        return ToActionResult(await _service.StartAsPatientAsync(caller, doctorId, ct),
            "Yalnız randevunuz olan doktora yazabilirsiniz.");
    }

    // Doktor başlatır: bu hastayla randevusu olmalı. doctor_id'si olmayan Doctor kullanıcısı 403.
    [Authorize(Policy = HastanePolicies.DoctorOnly)]
    [HttpPost("conversations/with-patient")]
    public async Task<ActionResult<ConversationDto>> StartWithPatient(StartConversationWithPatientDto dto, CancellationToken ct)
    {
        var caller = Caller();
        if (caller is null) return Unauthorized();
        if (string.IsNullOrEmpty(caller.DoctorId))
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Yetkiniz yok", detail: "Hesabınız bir doktor kaydına bağlı değil.");
        var patientUserId = dto.PatientUserId.Trim();
        if (patientUserId.Length == 0) return ValidationFailure(nameof(dto.PatientUserId), "patientUserId zorunludur.");

        return ToActionResult(await _service.StartAsDoctorAsync(caller, patientUserId, ct),
            "Yalnız randevusu olan hastaya yazabilirsiniz.");
    }

    // ---- liste / okunmamış ----

    // Patient: kendi konuşmaları; Doctor: doctor_id'si olanlar. En son mesaj üstte; UnreadCount benim tarafımın sayısı.
    [Authorize(Roles = PatientOrDoctor)]
    [HttpGet("conversations")]
    public async Task<ActionResult<List<ConversationDto>>> List(CancellationToken ct)
    {
        var caller = Caller();
        return caller is null ? Unauthorized() : Ok(await _service.ListAsync(caller, ct));
    }

    [Authorize(Roles = PatientOrDoctor)]
    [HttpGet("unread")]
    public async Task<ActionResult<UnreadTotalDto>> Unread(CancellationToken ct)
    {
        var caller = Caller();
        return caller is null ? Unauthorized() : Ok(new UnreadTotalDto { Total = await _service.GetUnreadTotalAsync(caller, ct) });
    }

    // ---- mesajlar ----

    // ?after=ISO ile yalnız o andan SONRAKİ mesajlar (arayüz yoklaması); after yoksa son `limit` mesaj. Saat dilimi (Z) verin.
    // Konuşma yok ya da çağıran taraf değil: ikisi de 404.
    [Authorize(Roles = PatientOrDoctor)]
    [HttpGet("conversations/{id}/messages")]
    public async Task<ActionResult<List<MessageDto>>> GetMessages(
        string id, [FromQuery] DateTimeOffset? after, [FromQuery] int limit = DefaultLimit, CancellationToken ct = default)
    {
        var caller = Caller();
        if (caller is null) return Unauthorized();
        if (limit < 1 || limit > MaxLimit) return ValidationFailure(nameof(limit), $"limit 1 ile {MaxLimit} arasında olmalı.");

        var messages = await _service.GetMessagesAsync(id, caller, after?.UtcDateTime, limit, ct);
        return messages is null ? NotFound() : Ok(messages);
    }

    [Authorize(Roles = PatientOrDoctor)]
    [HttpPost("conversations/{id}/messages")]
    public async Task<ActionResult<MessageDto>> Send(string id, SendMessageDto dto, CancellationToken ct)
    {
        var caller = Caller();
        if (caller is null) return Unauthorized();

        var text = dto.Text.Trim();
        if (text.Length is < 1 or > MaxTextLength)
            return ValidationFailure(nameof(dto.Text), $"Mesaj 1 ile {MaxTextLength} karakter arasında olmalı.");

        var message = await _service.SendAsync(id, caller, text, ct);
        return message is null ? NotFound() : Created($"api/chat/conversations/{id}/messages?after={message.SentAt:O}", message);
    }

    [Authorize(Roles = PatientOrDoctor)]
    [HttpPost("conversations/{id}/read")]
    public async Task<IActionResult> MarkRead(string id, CancellationToken ct)
    {
        var caller = Caller();
        if (caller is null) return Unauthorized();
        return await _service.MarkReadAsync(id, caller, ct) ? NoContent() : NotFound();
    }

    // ---- yardımcılar ----

    // ICurrentUser'da ad yok (Shared'a dokunulmaz): "name" claim'i doğrudan token'dan okunur; yoksa e-posta.
    private ChatCaller? Caller()
    {
        var userId = _currentUser.UserId;
        if (string.IsNullOrEmpty(userId)) return null;
        var name = User.FindFirst(HastaneClaims.Name)?.Value;
        if (string.IsNullOrWhiteSpace(name)) name = _currentUser.Email;
        return new ChatCaller(userId, _currentUser.DoctorId,
            _currentUser.IsInRole(HastaneRoles.Patient), _currentUser.IsInRole(HastaneRoles.Doctor), name?.Trim() ?? string.Empty);
    }

    private ActionResult<ConversationDto> ToActionResult(StartResult result, string noAppointmentMessage) => result.Status switch
    {
        StartStatus.Created => Created($"api/chat/conversations/{result.Conversation!.Id}/messages", result.Conversation),
        StartStatus.Existing => Ok(result.Conversation),
        StartStatus.NoAppointment => Problem(statusCode: StatusCodes.Status403Forbidden, title: "Yetkiniz yok", detail: noAppointmentMessage),
        StartStatus.DoctorNotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Doktor bulunamadı"),
        _ => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Servis kullanılamıyor",
            detail: "Randevu ya da doktor bilgisi şu an doğrulanamadı; lütfen tekrar deneyin."),
    };

    private ActionResult ValidationFailure(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
