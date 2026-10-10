using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Patient;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Patient.Controllers;

public class HomeController(
    IPatientAppointmentClient appointments, IPatientPrescriptionClient prescriptions, IPublicDoctorClient doctors) : PatientAreaController
{
    private const int PanelCount = 5;

    // Sınıf düzeyindeki alan kapısı (Patient) geçerli; action'a ek etiket gerekmez.
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Panel iki bağımsız servise dayanır; biri kapalıysa öbürü yine görünsün (kapalı olan için uyarı).
        var upcomingResult = await appointments.ListAsync(null, DateTime.UtcNow, ApiFailures.Unavailable, ct);
        var prescriptionResult = await prescriptions.ListAsync(ApiFailures.Unavailable, ct);

        var upcoming = (upcomingResult.Value ?? [])
            .Where(a => a.Status is "Pending" or "Confirmed")
            .OrderBy(a => a.AppointmentDate)
            .Take(PanelCount)
            .ToList();
        var recent = (prescriptionResult.Value ?? []).Take(PanelCount).ToList();

        var names = await DoctorNameLookup.LoadAsync(doctors, upcoming.Select(a => a.DoctorId).Concat(recent.Select(p => p.DoctorId)), ct);

        return View(new PatientHomeViewModel
        {
            Upcoming = upcoming.Select(a => new AppointmentRowViewModel { Appointment = a, DoctorName = names.NameOf(a.DoctorId) }).ToList(),
            Prescriptions = recent.Select(p => new PrescriptionRowViewModel { Prescription = p, DoctorName = names.NameOf(p.DoctorId) }).ToList(),
            AppointmentsUnavailable = !upcomingResult.IsSuccess,
            PrescriptionsUnavailable = !prescriptionResult.IsSuccess,
        });
    }
}
