using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Patient;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Patient.Controllers;

public class PrescriptionsController(IPatientPrescriptionClient prescriptions, IPublicDoctorClient doctors) : PatientAreaController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await prescriptions.ListAsync(ApiFailures.None, ct);
        var items = result.Value ?? [];
        var names = await DoctorNameLookup.LoadAsync(doctors, items.Select(p => p.DoctorId), ct);
        return View(items.Select(p => new PrescriptionRowViewModel { Prescription = p, DoctorName = names.NameOf(p.DoctorId) }).ToList());
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var result = await prescriptions.GetAsync(id, ct);
        var prescription = result.Value!;
        var names = await DoctorNameLookup.LoadAsync(doctors, [prescription.DoctorId], ct);
        return View(new PrescriptionDetailViewModel { Prescription = prescription, DoctorName = names.NameOf(prescription.DoctorId) });
    }

    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        var result = await prescriptions.GetAsync(id, ct);
        var prescription = result.Value!;
        var names = await DoctorNameLookup.LoadAsync(doctors, [prescription.DoctorId], ct);
        return View(new PrescriptionDetailViewModel { Prescription = prescription, DoctorName = names.NameOf(prescription.DoctorId) });
    }
}
