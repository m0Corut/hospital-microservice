using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Admin;
using Hastane.WebUI.ViewModels.DoctorPanel;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Admin reçeteleri görüntüler, düzenler, siler ve yazdırır (API: DoctorOrAdmin). Yeni reçete yazmak doktorun işidir.
public class PrescriptionsController(IAdminPrescriptionClient prescriptions, IAdminDoctorClient doctors, IAdminBranchClient branches) : AdminAreaController
{
    [HttpGet]
    public async Task<IActionResult> Index(PrescriptionFilterViewModel filter, CancellationToken ct)
    {
        // appointmentId Guid olmalı; geçersiz metin API'de 400 olur, ekranda sessizce yok sayılır.
        var appointmentId = Guid.TryParse(filter.AppointmentId, out var a) ? a.ToString() : null;
        var result = await prescriptions.GetAllAsync(filter.PatientId, filter.DoctorId, appointmentId, ct);

        return View(new PrescriptionIndexViewModel
        {
            Filter = filter,
            Items = result.Value ?? [],
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var result = await prescriptions.GetAsync(id, ct);
        if (!result.TryGetValue(out var prescription)) return NotFound();

        return View(new PrescriptionDetailsViewModel
        {
            Prescription = prescription,
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }

    // Yazdırılabilir görünüm: menüsüz sade sayfa.
    [HttpGet]
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        var result = await prescriptions.GetAsync(id, ct);
        if (!result.TryGetValue(out var prescription)) return NotFound();

        return View(new PrescriptionDetailsViewModel
        {
            Prescription = prescription,
            Lookups = await AdminLookups.LoadAsync(doctors, branches, ct),
        });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var result = await prescriptions.GetAsync(id, ct);
        if (!result.TryGetValue(out var p)) return NotFound();

        return View(new PrescriptionFormViewModel
        {
            PrescriptionId = p.Id,
            AppointmentId = p.AppointmentId,
            PatientLabel = "Hasta Id: " + p.PatientId,
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
        // Yol kimliği esastır; randevu ve hasta bilgisi formdan değil API'den okunur.
        var existing = await prescriptions.GetAsync(id, ct);
        if (!existing.TryGetValue(out var current)) return NotFound();

        model.PrescriptionId = id;
        model.AppointmentId = current.AppointmentId;
        model.PatientLabel = "Hasta Id: " + current.PatientId;

        if (PrescriptionFormOperations.ApplyItemOperation(ModelState, model, op)) return View(model);

        if (ModelState.IsValid && PrescriptionFormOperations.ValidateItemCount(ModelState, model))
        {
            var result = await prescriptions.UpdateAsync(id, model.Notes, PrescriptionFormOperations.ToBody(model), ct);
            if (result.IsSuccess)
            {
                SetSuccess("Reçete güncellendi.");
                return RedirectToAction(nameof(Details), new { id });
            }

            ModelState.AddApiProblem(result, "Reçete güncellenemedi.");
        }

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await prescriptions.DeleteAsync(id, ct);
        if (!result.IsSuccess)
        {
            SetWarning(AdminDisplay.ProblemText(result, "Reçete silinemedi."));
            return RedirectToAction(nameof(Details), new { id });
        }

        SetSuccess("Reçete silindi.");
        return RedirectToAction(nameof(Index));
    }
}
