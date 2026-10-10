using System.Globalization;
using Hastane.WebUI.Clients;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Admin tüm randevuları görür (API rol + sahiplik kuralı: Admin hepsini). Düzenleme/durum değiştirme bu ekranda yok.
public class AppointmentsController(IAdminAppointmentClient appointments, IAdminDoctorClient doctors, IAdminBranchClient branches) : AdminAreaController
{
    private static readonly string[] Statuses = ["Pending", "Confirmed", "Cancelled", "Completed"];

    [HttpGet]
    public async Task<IActionResult> Index(AppointmentFilterViewModel filter, CancellationToken ct)
    {
        // Tarih kutusu "yyyy-MM-dd" gönderir; "to" günün sonuna çekilir ki o günün randevuları da gelsin.
        var from = DateTime.TryParse(filter.From, CultureInfo.InvariantCulture, out var f) ? f.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        var to = DateTime.TryParse(filter.To, CultureInfo.InvariantCulture, out var t) ? t.ToString("yyyy-MM-dd'T'23:59:59", CultureInfo.InvariantCulture) : null;
        var status = Statuses.Contains(filter.Status) ? filter.Status : null;

        var result = await appointments.GetAllAsync(filter.DoctorId, filter.PatientId, status, from, to, ct);
        return View(new AppointmentIndexViewModel
        {
            Filter = filter,
            Items = result.Value ?? [],
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var result = await appointments.GetAsync(id, ct);
        if (!result.TryGetValue(out var appointment)) return NotFound();

        return View(new AppointmentDetailsViewModel
        {
            Appointment = appointment,
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }

    // Onay sayfası: yalnız gösterir, silmez.
    [HttpGet]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await appointments.GetAsync(id, ct);
        if (!result.TryGetValue(out var appointment)) return NotFound();

        return View(new AppointmentDetailsViewModel
        {
            Appointment = appointment,
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken ct)
    {
        var result = await appointments.DeleteAsync(id, ct);
        if (!result.IsSuccess)
        {
            SetWarning(AdminDisplay.ProblemText(result, "Randevu silinemedi."));
            return RedirectToAction(nameof(Index));
        }

        SetSuccess("Randevu silindi.");
        return RedirectToAction(nameof(Index));
    }
}
