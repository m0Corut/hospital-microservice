using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

public class PrescriptionsController(
    IDoctorPanelPrescriptionClient prescriptions,
    IDoctorPanelAppointmentClient appointments) : DoctorPanelController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var list = (await prescriptions.ListAsync(DoctorId, null, ct)).Value ?? [];
        var patientLabel = await PatientLabelsAsync(list.Select(p => p.PatientId), ct);

        return View(list.OrderByDescending(p => p.IssuedAt).Select(p => new PrescriptionListRow
        {
            Id = p.Id,
            AppointmentId = p.AppointmentId,
            IssuedAt = p.IssuedAt,
            PatientLabel = patientLabel(p.PatientId),
            ItemCount = p.ItemCount,
        }).ToList());
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var prescription = (await prescriptions.GetAsync(id, ct)).Value!;
        var patientLabel = await PatientLabelsAsync([prescription.PatientId], ct);

        return View(new PrescriptionDetailViewModel
        {
            Id = prescription.Id,
            AppointmentId = prescription.AppointmentId,
            IssuedAt = prescription.IssuedAt,
            PatientLabel = patientLabel(prescription.PatientId),
            Notes = prescription.Notes,
            Items = prescription.Items.OrderBy(i => i.LineNo).ToList(),
        });
    }

    // Yazdırılabilir görünüm: menüsüz sade sayfa. Sahiplik Details ile aynı (başkasının reçetesi API'de 404).
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        var prescription = (await prescriptions.GetAsync(id, ct)).Value!;
        var patientLabel = await PatientLabelsAsync([prescription.PatientId], ct);

        return View(new PrescriptionDetailViewModel
        {
            Id = prescription.Id,
            AppointmentId = prescription.AppointmentId,
            IssuedAt = prescription.IssuedAt,
            PatientLabel = patientLabel(prescription.PatientId),
            Notes = prescription.Notes,
            Items = prescription.Items.OrderBy(i => i.LineNo).ToList(),
        });
    }

    // Randevudan "Reçete yaz": randevu, hasta ve doktor sunucuda randevudan okunur; formdan yalnız randevu kimliği gelir.
    public async Task<IActionResult> Create(Guid appointmentId, CancellationToken ct)
    {
        var appointment = (await appointments.GetAsync(appointmentId, ct)).Value!;
        if (!DoctorPanelFormat.CanWritePrescription(appointment.Status))
        {
            SetWarning("Reçete yalnız onaylanmış ya da tamamlanmış randevu için yazılabilir.");
            return RedirectToAction("Details", "Appointments", new { id = appointmentId });
        }

        var patientLabel = await PatientLabelsAsync([appointment.PatientId], ct);
        return View("Form", new PrescriptionFormViewModel
        {
            AppointmentId = appointmentId,
            PatientLabel = patientLabel(appointment.PatientId),
            Items = [new PrescriptionItemForm()],
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(PrescriptionFormViewModel model, string? op, CancellationToken ct)
    {
        var appointment = (await appointments.GetAsync(model.AppointmentId, ct)).Value!;
        if (!DoctorPanelFormat.CanWritePrescription(appointment.Status))
        {
            SetWarning("Reçete yalnız onaylanmış ya da tamamlanmış randevu için yazılabilir.");
            return RedirectToAction("Details", "Appointments", new { id = model.AppointmentId });
        }

        var patientLabel = await PatientLabelsAsync([appointment.PatientId], ct);
        model.PatientLabel = patientLabel(appointment.PatientId);

        if (PrescriptionFormOperations.ApplyItemOperation(ModelState, model, op)) return View("Form", model);

        if (ModelState.IsValid && PrescriptionFormOperations.ValidateItemCount(ModelState, model))
        {
            // PatientId formdan DEĞİL, az önce API'den okunan randevudan gelir; DoctorId'yi servis token'dan alır.
            var result = await prescriptions.CreateAsync(model.AppointmentId, appointment.PatientId, model.Notes, PrescriptionFormOperations.ToBody(model), ct);
            if (result.TryGetValue(out var created))
            {
                SetSuccess("Reçete yazıldı.");
                return RedirectToAction(nameof(Details), new { id = created.Id });
            }

            ModelState.AddApiProblem(result);
        }

        return View("Form", model);
    }

    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var p = (await prescriptions.GetAsync(id, ct)).Value!;
        var patientLabel = await PatientLabelsAsync([p.PatientId], ct);

        return View("Form", new PrescriptionFormViewModel
        {
            PrescriptionId = p.Id,
            AppointmentId = p.AppointmentId,
            PatientLabel = patientLabel(p.PatientId),
            Notes = p.Notes,
            Items = p.Items.OrderBy(i => i.LineNo).Select(i => new PrescriptionItemForm
            {
                MedicineName = i.MedicineName,
                Dosage = i.Dosage,
                Frequency = i.Frequency,
                DurationDays = i.DurationDays,
                Instructions = i.Instructions,
            }).ToList(),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, PrescriptionFormViewModel model, string? op, CancellationToken ct)
    {
        // Yol kimliği esastır; formdaki gizli alana güvenilmez. Reçete sahibi mi? API 404/403 ile söyler.
        var existing = (await prescriptions.GetAsync(id, ct)).Value!;
        var patientLabel = await PatientLabelsAsync([existing.PatientId], ct);
        model.PrescriptionId = id;
        model.AppointmentId = existing.AppointmentId;
        model.PatientLabel = patientLabel(existing.PatientId);

        if (PrescriptionFormOperations.ApplyItemOperation(ModelState, model, op)) return View("Form", model);

        if (ModelState.IsValid && PrescriptionFormOperations.ValidateItemCount(ModelState, model))
        {
            var result = await prescriptions.UpdateAsync(id, model.Notes, PrescriptionFormOperations.ToBody(model), ct);
            if (result.IsSuccess)
            {
                SetSuccess("Reçete güncellendi.");
                return RedirectToAction(nameof(Details), new { id });
            }

            ModelState.AddApiProblem(result);
        }

        return View("Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await prescriptions.DeleteAsync(id, ct);
        if (!result.IsSuccess)
        {
            SetWarning(result.Problem?.Detail ?? result.Problem?.Title ?? "Reçete silinemedi.");
            return RedirectToAction(nameof(Details), new { id });
        }

        SetSuccess("Reçete silindi.");
        return RedirectToAction(nameof(Index));
    }
}
