using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Controllers;

// Bildirim listesi (tüm giriş yapmış roller). Alıcı token'dan bulunur; WebUI hiçbir kimlik göndermez.
// Okundu işaretleri POST'tur (GET ile veri değişmez); antiforgery global filtreyle.
[Authorize]
[Route("Notifications")]
public class NotificationsController(INotificationClient notifications) : AppController
{
    private const int PageSize = 20;

    [HttpGet("")]
    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 10_000);
        var result = await notifications.GetMineAsync(false, page, PageSize, ApiFailures.None, ct);
        var data = result.Value!;

        return View(new NotificationsViewModel
        {
            Page = page,
            PageSize = PageSize,
            TotalCount = data.TotalCount,
            UnreadCount = data.UnreadCount,
            Items = data.Items.Select(n => new NotificationRowViewModel
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                SafeLink = SafeLocalLink(n.Link),
                CreatedAt = n.CreatedAt,
                IsRead = n.ReadAt is not null,
            }).ToList(),
        });
    }

    [HttpPost("{id}/Read")]
    public async Task<IActionResult> Read(string id, int page = 1, CancellationToken ct = default)
    {
        // 404 (başkasının ya da silinmiş bildirim) sessizce geçilir: liste zaten yenilenir.
        await notifications.MarkReadAsync(id, ApiFailures.NotFound, ct);
        return RedirectToAction(nameof(Index), new { page });
    }

    [HttpPost("ReadAll")]
    public async Task<IActionResult> ReadAll(CancellationToken ct = default)
    {
        await notifications.MarkAllReadAsync(ct);
        SetSuccess("Tüm bildirimler okundu olarak işaretlendi.");
        return RedirectToAction(nameof(Index));
    }

    // Bağlantıya tıklama: ÖNCE okundu yap, SONRA bağlantıya git. Bağlantı formdan gelir ama yerel değilse yok sayılır
    // (open redirect yok): sunucu servisi yalnız göreli yol kaydeder, ama istemciden gelen değere yine de güvenilmez.
    [HttpPost("{id}/Open")]
    public async Task<IActionResult> Open(string id, string? link, int page = 1, CancellationToken ct = default)
    {
        await notifications.MarkReadAsync(id, ApiFailures.NotFound, ct);
        return SafeLocalLink(link) is { } safe ? LocalRedirect(safe) : RedirectToAction(nameof(Index), new { page });
    }

    // "/" ile başlayan yerel yol; "//evil", "/\evil", "https://evil", "javascript:" ve boş değer elenir.
    private string? SafeLocalLink(string? link)
        => !string.IsNullOrWhiteSpace(link) && link.StartsWith('/') && Url.IsLocalUrl(link) ? link : null;
}
