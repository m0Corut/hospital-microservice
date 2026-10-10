using Hastane.Notification.Api.Dtos;
using Hastane.Notification.Api.Services;
using Hastane.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.Notification.Api.Controllers;

// Sınıf düzeyinde yetki YOK, bilerek: her eylem etiketini açıkça taşır (bkz. ReviewsController).
[ApiController]
[Route("api/notifications")]
public class NotificationsController(INotificationService service, ICurrentUser user) : ControllerBase
{
    // Alıcı kimliği İSTEMCİDEN alınmaz: hasta bildirimleri token "sub", doktor bildirimleri token "doctor_id" ile eşleşir.
    [Authorize(Policy = HastanePolicies.AuthenticatedUser)]
    [HttpGet("me")]
    public async Task<ActionResult<NotificationPageDto>> GetMine(
        CancellationToken ct, [FromQuery] bool unreadOnly = false, [FromQuery] int page = 1,
        [FromQuery] int pageSize = NotificationService.DefaultPageSize)
        => Ok(await service.GetMineAsync(user.UserId, user.DoctorId, unreadOnly, page, pageSize, ct));

    // Başkasının bildirimi "yok" ile aynı 404: varlığı sızdırmaz.
    [Authorize(Policy = HastanePolicies.AuthenticatedUser)]
    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkRead(string id, CancellationToken ct)
        => await service.MarkReadAsync(id, user.UserId, user.DoctorId, ct) ? NoContent() : NotFound();

    [Authorize(Policy = HastanePolicies.AuthenticatedUser)]
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await service.MarkAllReadAsync(user.UserId, user.DoctorId, ct);
        return NoContent();
    }
}
