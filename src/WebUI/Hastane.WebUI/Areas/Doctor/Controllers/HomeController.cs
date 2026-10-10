using Hastane.WebUI.Clients;
using Hastane.WebUI.Models.Api.DoctorPanel;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

public class HomeController(IDoctorPanelAppointmentClient appointments) : DoctorPanelController
{
    // Sınıf düzeyindeki alan kapısı (Doctor) geçerli; action'a ek etiket gerekmez.
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var todayStart = DateTime.Now.Date;
        var todayStartUtc = todayStart.ToUniversalTime();

        // İki çağrı: bugünden sonrası (bugün + yaklaşan) ve bekleyenler (geçmiş tarihli bekleyen de görünsün diye ayrı).
        var upcomingResult = await appointments.ListAsync(DoctorId, null, todayStartUtc, null, ct);
        var pendingResult = await appointments.ListAsync(DoctorId, DoctorPanelAppointmentStatus.Pending, null, null, ct);

        var upcoming = upcomingResult.Value ?? [];
        var pending = pendingResult.Value ?? [];

        var branchLabel = await BranchLabelsAsync(ct);
        var patientLabel = await PatientLabelsAsync(upcoming.Concat(pending).Select(a => a.PatientId), ct);
        AppointmentRow Row(DoctorPanelAppointmentApiModel a)
            => new(a.Id, a.AppointmentDate, patientLabel(a.PatientId), branchLabel(a.BranchId), a.Status);

        var todayAll = upcoming
            .Where(a => DoctorPanelFormat.ToLocal(a.AppointmentDate).Date == todayStart && a.Status != DoctorPanelAppointmentStatus.Cancelled)
            .OrderBy(a => a.AppointmentDate).ToList();
        var upcomingAll = upcoming
            .Where(a => DoctorPanelFormat.ToLocal(a.AppointmentDate).Date > todayStart
                        && a.Status is DoctorPanelAppointmentStatus.Pending or DoctorPanelAppointmentStatus.Confirmed)
            .OrderBy(a => a.AppointmentDate).ToList();

        var model = new DoctorHomeViewModel
        {
            Today = todayAll.Select(Row).ToList(),
            Upcoming = upcomingAll.Take(8).Select(Row).ToList(),
            Pending = pending.OrderBy(a => a.AppointmentDate).Take(8).Select(Row).ToList(),
            UpcomingCount = upcomingAll.Count,
            PendingCount = pending.Count,
        };
        return View(model);
    }
}
