using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.DoctorPanel;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

public class AppointmentsController(
    IDoctorPanelAppointmentClient appointments,
    IDoctorPanelPrescriptionClient prescriptions) : DoctorPanelController
{
    public async Task<IActionResult> Index(AppointmentFilterViewModel filter, CancellationToken ct)
    {
        // Tarih süzgeci yerel gün sınırlarıyla UTC'ye çevrilir; "bitiş" günün sonunu da kapsar.
        var fromUtc = filter.From is { } f ? f.Date.ToUniversalTime() : (DateTime?)null;
        var toUtc = filter.To is { } t ? t.Date.AddDays(1).AddTicks(-1).ToUniversalTime() : (DateTime?)null;

        var result = await appointments.ListAsync(DoctorId, filter.Status, fromUtc, toUtc, ct);
        var list = (result.Value ?? []).OrderByDescending(a => a.AppointmentDate).ToList();

        var branchLabel = await BranchLabelsAsync(ct);
        var patientLabel = await PatientLabelsAsync(list.Select(a => a.PatientId), ct);

        return View(new AppointmentListViewModel
        {
            Filter = filter,
            Rows = list.Select(a => new AppointmentRow(a.Id, a.AppointmentDate, patientLabel(a.PatientId), branchLabel(a.BranchId), a.Status)).ToList(),
        });
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
        => View(await BuildDetailAsync(id, null, ct));

    [HttpPost]
    public async Task<IActionResult> SetStatus(Guid id, DoctorPanelAppointmentStatus status, CancellationToken ct)
    {
        var result = await appointments.SetStatusAsync(id, status, ct);
        if (result.IsSuccess)
            SetSuccess("Randevu durumu güncellendi.");
        else
            SetWarning(result.Problem?.Detail ?? result.Problem?.Title ?? "Durum değiştirilemedi.");

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> SaveDetail(Guid id, AppointmentDetailForm form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            var result = await appointments.SaveDetailAsync(id, form.Complaint, form.Diagnosis, form.DoctorNotes, ct);
            if (result.IsSuccess)
            {
                SetSuccess("Muayene kaydı kaydedildi.");
                return RedirectToAction(nameof(Details), new { id });
            }

            ModelState.AddApiProblem(result);
        }

        return View(nameof(Details), await BuildDetailAsync(id, form, ct));
    }

    private async Task<AppointmentDetailViewModel> BuildDetailAsync(Guid id, AppointmentDetailForm? submitted, CancellationToken ct)
    {
        // Sahiplik backend'de: başkasının randevusu 404 döner, 404 sayfasına gideriz.
        var appointment = (await appointments.GetAsync(id, ct)).Value!;
        var written = (await prescriptions.ListAsync(DoctorId, id, ct)).Value ?? [];
        var patientLabel = await PatientLabelsAsync([appointment.PatientId], ct);
        var branchLabel = await BranchLabelsAsync(ct);

        return new AppointmentDetailViewModel
        {
            Id = appointment.Id,
            Date = appointment.AppointmentDate,
            CreatedAt = appointment.CreatedAt,
            Status = appointment.Status,
            PatientLabel = patientLabel(appointment.PatientId),
            BranchLabel = branchLabel(appointment.BranchId),
            DetailUpdatedAt = appointment.Detail?.UpdatedAt,
            Form = submitted ?? new AppointmentDetailForm
            {
                Complaint = appointment.Detail?.Complaint ?? string.Empty,
                Diagnosis = appointment.Detail?.Diagnosis,
                DoctorNotes = appointment.Detail?.DoctorNotes,
            },
            Prescriptions = written.OrderByDescending(p => p.IssuedAt)
                .Select(p => new PrescriptionLinkRow { Id = p.Id, IssuedAt = p.IssuedAt, ItemCount = p.ItemCount }).ToList(),
        };
    }
}
