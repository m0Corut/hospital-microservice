using Hastane.WebUI.Clients;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

public class PatientsController(IDoctorPanelAppointmentClient appointments) : DoctorPanelController
{
    // "Hastalarım": ayrı bir hasta ucu yok; doktorun randevularından benzersiz hasta listesi türetilir.
    // Ad için TEK toplu users?ids= çağrısı (N+1 yok); çözülemeyen kimlik "Hasta #1a2b3c4d" olarak görünür.
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var list = (await appointments.ListAsync(DoctorId, null, null, null, ct)).Value ?? [];

        var groups = list
            .GroupBy(a => a.PatientId)
            .Select(g => (PatientId: g.Key, Count: g.Count(), Last: g.OrderByDescending(a => a.AppointmentDate).First()))
            .OrderByDescending(x => x.Last.AppointmentDate)
            .ToList();

        var label = await PatientLabelsAsync(groups.Select(g => g.PatientId), ct);
        return View(groups.Select(g => new PatientRow(label(g.PatientId), g.Count, g.Last.AppointmentDate, g.Last.Id)).ToList());
    }
}
