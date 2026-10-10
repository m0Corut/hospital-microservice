using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Queue;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hastane.WebUI.Areas.Patient.Controllers;

public class QueueController(
    IQueueClient queue, IPublicDoctorClient doctors, IOptions<GatewayOptions> gateway, IConfiguration configuration) : PatientAreaController
{
    // Sıra numaram + önümde kaç kişi. Sınıf düzeyindeki alan kapısı (Patient) geçerli.
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await queue.MineTodayAsync(ct);
        var entries = (result.Value ?? []).OrderBy(e => e.CreatedAt).ToList();
        var names = await DoctorNameLookup.LoadAsync(doctors, entries.Select(e => e.DoctorId), ct);

        return View(new PatientQueueViewModel
        {
            Rows = entries.Select(e => new PatientQueueRow(e, names.NameOf(e.DoctorId))).ToList(),
            Unavailable = !result.IsSuccess,
            HubUrl = QueueText.HubUrl(gateway.Value.BaseUrl, configuration["Gateway:PublicBaseUrl"]),
        });
    }

    // Randevu kimliği formdan gelir; hasta kimliği ve ad token'dan (API). İş kuralları API'de: onaylı + bugün + hastanın kendisi.
    [HttpPost]
    public async Task<IActionResult> CheckIn(Guid appointmentId, CancellationToken ct)
    {
        if (appointmentId == Guid.Empty)
        {
            SetWarning("Randevu seçilmedi.");
            return RedirectToAction("Index", "Appointments");
        }

        var result = await queue.CheckInAsync(appointmentId, ct);
        if (result.IsSuccess)
        {
            SetSuccess(result.StatusCode == StatusCodes.Status201Created
                ? $"Sıraya girdiniz. Sıra numaranız: {result.Value?.Number}"
                : "Zaten sıradasınız.");
            return RedirectToAction(nameof(Index));
        }

        // 409 (onaylı değil / bugün değil), 503 (randevu servisi kapalı): açıklayıcı mesaj, randevu sayfasına dön.
        SetWarning(result.Problem?.Detail ?? "Sıraya girilemedi.");
        return RedirectToAction("Details", "Appointments", new { id = appointmentId });
    }
}
