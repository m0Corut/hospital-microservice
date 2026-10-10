using Hastane.WebUI.Clients;
using Hastane.WebUI.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Admin.Controllers;

// Reçeteler salt okunur: yazma/düzenleme doktorun işidir.
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
}
