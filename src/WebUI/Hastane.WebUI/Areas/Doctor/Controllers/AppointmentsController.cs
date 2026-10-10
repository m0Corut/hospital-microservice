using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.DoctorPanel;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Hastane.WebUI.ViewModels.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

public class AppointmentsController(
    IDoctorPanelAppointmentClient appointments,
    IDoctorPanelPrescriptionClient prescriptions,
    IAppointmentRescheduleClient reschedule,
    IAppointmentAvailabilityClient availability) : DoctorPanelController
{
    public async Task<IActionResult> Index(AppointmentFilterViewModel filter, CancellationToken ct)
    {
        // Tarih süzgeci yerel gün sınırlarıyla UTC'ye çevrilir; "bitiş" günün sonunu da kapsar.
        var fromUtc = filter.From is { } f ? AppointmentTime.ClinicLocalToUtc(f.Date) : (DateTime?)null;
        var toUtc = filter.To is { } t ? AppointmentTime.ClinicLocalToUtc(t.Date.AddDays(1)).AddTicks(-1) : (DateTime?)null;

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

    // Yeniden planlama: yalnız bekleyen / onaylı randevu. Sahiplik (başkasının randevusu 404) ve çakışma (409) API'dedir.
    [HttpGet]
    public async Task<IActionResult> Reschedule(Guid id, string? date, CancellationToken ct)
    {
        var appointment = (await appointments.GetAsync(id, ct)).Value!;
        if (!DoctorPanelFormat.CanReschedule(appointment.Status))
        {
            SetWarning("Yalnız bekleyen ya da onaylanmış randevu yeniden planlanabilir.");
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(await BuildRescheduleAsync(appointment, new RescheduleViewModel { Id = id, Date = date }, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Reschedule(Guid id, RescheduleViewModel model, CancellationToken ct)
    {
        // Şube, hasta ve durum formdan değil API'den alınır.
        var appointment = (await appointments.GetAsync(id, ct)).Value!;
        model.Id = id;
        var utc = AppointmentTime.ParseSlot(model.SlotStart, ModelState, nameof(model.SlotStart));

        if (ModelState.IsValid && utc is { } newDate)
        {
            var result = await reschedule.RescheduleAsync(id, appointment.BranchId, newDate, ct);
            if (result.IsSuccess)
            {
                SetSuccess("Randevu yeniden planlandı.");
                return RedirectToAction(nameof(Details), new { id });
            }

            // 400: çalışma programı dışı saat; 409: dolu saat / değiştirilemez; 503: doktor servisi kapalı.
            ModelState.AddBookingProblem(result, "Randevu yeniden planlanamadı.");
        }

        return View(await BuildRescheduleAsync(appointment, model, ct));
    }

    private async Task<RescheduleViewModel> BuildRescheduleAsync(DoctorPanelAppointmentApiModel appointment, RescheduleViewModel model, CancellationToken ct)
    {
        var patientLabel = await PatientLabelsAsync([appointment.PatientId], ct);
        model.PartyLabel = patientLabel(appointment.PatientId);
        model.CurrentDate = appointment.AppointmentDate;
        model.Picker = await AppointmentTime.BuildPickerAsync(availability, appointment.DoctorId, model.Date,
            day => Url.Action(nameof(Reschedule), new { id = model.Id, date = AppointmentTime.DateText(day) })!, ct);
        model.Picker.SelectedStart = model.SlotStart;
        return model;
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
            Note = appointment.Note,
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
