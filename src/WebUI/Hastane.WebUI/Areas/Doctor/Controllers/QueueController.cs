using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Queue;
using Hastane.WebUI.ViewModels.Queue;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

public class QueueController(IQueueClient queue, IOptions<GatewayOptions> gateway, IConfiguration configuration) : DoctorPanelController
{
    // Bugünün listesi. Doktorun kendisi token'dan; sınıf düzeyindeki alan kapısı (Doctor) geçerli.
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await queue.DoctorTodayAsync(ct);
        var all = (result.Value ?? []).OrderBy(e => e.Number).ToList();

        return View(new DoctorQueueViewModel
        {
            DoctorId = DoctorId,
            Current = all.FirstOrDefault(e => e.Status == "Called"),
            Waiting = all.Where(e => e.Status == "Waiting").ToList(),
            Finished = all.Where(e => e.Status is "Completed" or "NoShow").ToList(),
            Unavailable = !result.IsSuccess,
            HubUrl = QueueText.HubUrl(gateway.Value.BaseUrl, configuration["Gateway:PublicBaseUrl"]),
        });
    }

    [HttpPost]
    public async Task<IActionResult> CallNext(CancellationToken ct)
        => Done(await queue.CallNextAsync(ct), e => $"Sıradaki çağrıldı: {e.Number} · {e.DisplayName}");

    [HttpPost]
    public async Task<IActionResult> Call(Guid id, CancellationToken ct)
        => Done(await queue.CallAsync(id, ct), e => $"Çağrıldı: {e.Number} · {e.DisplayName}");

    [HttpPost]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
        => Done(await queue.CompleteAsync(id, ct), e => $"Tamamlandı: {e.Number} · {e.DisplayName}");

    [HttpPost]
    public async Task<IActionResult> NoShow(Guid id, CancellationToken ct)
        => Done(await queue.NoShowAsync(id, ct), e => $"Gelmedi olarak işaretlendi: {e.Number} · {e.DisplayName}");

    // Başarı: bilgi; 409 (zaten çağrılmış kişi var, durum uymuyor) ya da 503: API'nin açıklaması uyarı olarak görünür.
    // Her durumda PRG: listeye dön (sayfa yenilenince eylem tekrarlanmaz).
    private IActionResult Done(ApiResult<QueueEntryApiModel> result, Func<QueueEntryApiModel, string> message)
    {
        if (result.TryGetValue(out var entry)) SetSuccess(message(entry));
        else SetWarning(result.Problem?.Detail ?? "İşlem tamamlanamadı.");
        return RedirectToAction(nameof(Index));
    }
}
