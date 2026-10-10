using Hastane.WebUI.Clients;
using Hastane.WebUI.Models.Api.DoctorPanel;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

// Rotalar özniteliklidir: /Doctor/Patients (liste) ve /Doctor/Patients/{patientId} (hasta profili). "Index" sabit parça olduğundan
// bir hasta kimliğiyle çakışmaz (sabit parça, parametreden önce eşleşir).
public class PatientsController(
    IDoctorPanelAppointmentClient appointments,
    IDoctorPanelPrescriptionClient prescriptions,
    IDoctorPanelUserClient users) : DoctorPanelController
{
    // "Hastalarım": ayrı bir hasta ucu yok; doktorun randevularından benzersiz hasta listesi türetilir.
    // Ad için TEK toplu users?ids= çağrısı (N+1 yok); çözülemeyen kimlik "Hasta #1a2b3c4d" olarak görünür.
    [HttpGet("Doctor/Patients")]
    [HttpGet("Doctor/Patients/Index")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var list = (await appointments.ListAsync(DoctorId, null, null, null, ct)).Value ?? [];

        var groups = list
            .GroupBy(a => a.PatientId)
            .Select(g => (PatientId: g.Key, Count: g.Count(), Last: g.OrderByDescending(a => a.AppointmentDate).First()))
            .OrderByDescending(x => x.Last.AppointmentDate)
            .ToList();

        var label = await PatientLabelsAsync(groups.Select(g => g.PatientId), ct);
        return View(groups.Select(g => new PatientRow(label(g.PatientId), g.Count, g.Last.AppointmentDate, g.Last.Id, g.PatientId)).ToList());
    }

    // Hasta profili. Sahiplik KURALI serviste: randevu/reçete listeleri doctorId'yi token'dan zorlar, yani başka doktorun hastası için
    // bu listeler boş gelir. Bu doktorun o hastayla hiç randevusu yoksa "kayıt bulunamadı" (404): kullanıcı kaydı sızdırılmaz.
    [HttpGet("Doctor/Patients/{patientId}")]
    public async Task<IActionResult> Details(string patientId, CancellationToken ct)
    {
        var list = (await appointments.ListForPatientAsync(DoctorId, patientId, ct)).Value ?? [];
        // Servis süzgecine ek savunma: başka hastanın satırı sızsa bile gösterilmez.
        list = list.Where(a => string.Equals(a.PatientId, patientId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (list.Count == 0) return NotFound();

        var user = await users.GetAsync(patientId, ct);
        var rx = (await prescriptions.ListForPatientAsync(DoctorId, patientId, ct)).Value ?? [];
        rx = rx.Where(p => string.Equals(p.PatientId, patientId, StringComparison.OrdinalIgnoreCase)).ToList();
        var branchLabel = await BranchLabelsAsync(ct);

        var name = user is null ? "Hasta #" + DoctorPanelFormat.ShortId(patientId) : $"{user.FirstName} {user.LastName}".Trim();
        if (name.Length == 0) name = "Hasta #" + DoctorPanelFormat.ShortId(patientId);

        return View(new PatientProfileViewModel
        {
            PatientId = patientId,
            Name = name,
            Email = string.IsNullOrWhiteSpace(user?.Email) ? null : user!.Email,
            Appointments = list.OrderByDescending(a => a.AppointmentDate).Select(a => new PatientAppointmentRow
            {
                Id = a.Id,
                Date = a.AppointmentDate,
                BranchLabel = branchLabel(a.BranchId),
                Status = a.Status,
                Note = a.Note,
            }).ToList(),
            Prescriptions = rx.OrderByDescending(p => p.IssuedAt).Select(p => new PrescriptionListRow
            {
                Id = p.Id,
                AppointmentId = p.AppointmentId,
                IssuedAt = p.IssuedAt,
                PatientLabel = name,
                ItemCount = p.ItemCount,
            }).ToList(),
            CompletedCount = list.Count(a => a.Status == DoctorPanelAppointmentStatus.Completed),
            FirstVisit = list.Min(a => a.AppointmentDate),
            LastVisit = list.Max(a => a.AppointmentDate),
        });
    }
}
