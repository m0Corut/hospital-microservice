using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Doctor.Controllers;

public class PrescriptionsController(
    IDoctorPanelPrescriptionClient prescriptions,
    IDoctorPanelAppointmentClient appointments) : DoctorPanelController
{
    private const string AddItemOp = "add-item";
    private const string RemoveItemOpPrefix = "remove-item:";

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

        if (ApplyItemOperation(model, op)) return View("Form", model);

        if (ModelState.IsValid && ValidateItemCount(model))
        {
            // PatientId formdan DEĞİL, az önce API'den okunan randevudan gelir; DoctorId'yi servis token'dan alır.
            var result = await prescriptions.CreateAsync(model.AppointmentId, appointment.PatientId, model.Notes, ToBody(model), ct);
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

        if (ApplyItemOperation(model, op)) return View("Form", model);

        if (ModelState.IsValid && ValidateItemCount(model))
        {
            var result = await prescriptions.UpdateAsync(id, model.Notes, ToBody(model), ct);
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

    // "Kalem ekle / çıkar" düğmeleri formu JS'siz yeniden çizer. Dönüş true: işlem yapıldı, doğrulama/kaydetme atlanır.
    private bool ApplyItemOperation(PrescriptionFormViewModel model, string? op)
    {
        if (op == AddItemOp)
        {
            if (model.Items.Count < PrescriptionFormViewModel.MaxItems)
                model.Items.Add(new PrescriptionItemForm());
        }
        else if (op is not null && op.StartsWith(RemoveItemOpPrefix, StringComparison.Ordinal)
                 && int.TryParse(op[RemoveItemOpPrefix.Length..], out var index)
                 && index >= 0 && index < model.Items.Count)
        {
            model.Items.RemoveAt(index);
        }
        else
        {
            return false;
        }

        // Eski ModelState'teki "Items[2]..." girdileri kayan indekslerle yanlış satırı doldurmasın.
        ModelState.Clear();
        return true;
    }

    private bool ValidateItemCount(PrescriptionFormViewModel model)
    {
        if (model.Items.Count is >= 1 and <= PrescriptionFormViewModel.MaxItems) return true;
        ModelState.AddModelError(nameof(model.Items), $"Reçetede 1 ile {PrescriptionFormViewModel.MaxItems} arasında ilaç kalemi olmalı.");
        return false;
    }

    private static List<DoctorPanelPrescriptionItemBody> ToBody(PrescriptionFormViewModel model)
        => model.Items.Select(i => new DoctorPanelPrescriptionItemBody(i.MedicineName, i.Dosage, i.Frequency, i.DurationDays, i.Instructions)).ToList();
}
