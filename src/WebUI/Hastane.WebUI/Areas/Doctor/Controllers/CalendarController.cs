using Hastane.WebUI.Clients;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

public class CalendarController(IDoctorPanelAppointmentClient appointments) : DoctorPanelController
{
    // /Doctor/Calendar?start=2026-10-12: verilen günün haftası (Pazartesi-Pazar). Boşsa bu hafta.
    public async Task<IActionResult> Index(DateTime? start, CancellationToken ct)
    {
        var day = (start ?? DateTime.Now).Date;
        var weekStart = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

        var fromUtc = weekStart.ToUniversalTime();
        var toUtc = weekStart.AddDays(7).AddTicks(-1).ToUniversalTime();
        var list = (await appointments.ListAsync(DoctorId, null, fromUtc, toUtc, ct)).Value ?? [];
        var patientLabel = await PatientLabelsAsync(list.Select(a => a.PatientId), ct);

        var today = DateTime.Now.Date;
        var days = Enumerable.Range(0, 7).Select(i =>
        {
            var date = weekStart.AddDays(i);
            var events = list
                .Where(a => DoctorPanelFormat.ToLocal(a.AppointmentDate).Date == date)
                .OrderBy(a => a.AppointmentDate)
                .Select(a => new CalendarEvent(a.Id, DoctorPanelFormat.ToLocal(a.AppointmentDate), patientLabel(a.PatientId), a.Status))
                .ToList();
            return new CalendarDay(date, date == today, events);
        }).ToList();

        return View(new CalendarViewModel { WeekStart = weekStart, Days = days });
    }
}
