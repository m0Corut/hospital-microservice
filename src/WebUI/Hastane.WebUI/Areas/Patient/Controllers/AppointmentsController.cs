using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Patient;
using Hastane.WebUI.Models.Api.Public;
using Hastane.WebUI.ViewModels.Patient;
using Hastane.WebUI.ViewModels.Public;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Patient.Controllers;

public class AppointmentsController(
    IPatientAppointmentClient appointments, IPatientPaymentClient payments, IPublicDoctorClient doctors) : PatientAreaController
{
    private static readonly string[] Statuses = ["Pending", "Confirmed", "Cancelled", "Completed"];

    public async Task<IActionResult> Index(string? status, CancellationToken ct)
    {
        // Bilinmeyen durum değeri API'ye gitmez (400 yerine süzgeç yok sayılır).
        var filter = Statuses.Contains(status) ? status : null;
        var result = await appointments.ListAsync(filter, null, ApiFailures.None, ct);
        var items = (result.Value ?? []).OrderByDescending(a => a.AppointmentDate).ToList();

        var names = await DoctorNameLookup.LoadAsync(doctors, items.Select(a => a.DoctorId), ct);
        return View(new AppointmentListViewModel
        {
            Status = filter,
            Items = items.Select(a => new AppointmentRowViewModel { Appointment = a, DoctorName = names.NameOf(a.DoctorId) }).ToList(),
        });
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var result = await appointments.GetAsync(id, ct);
        var appointment = result.Value!;

        var names = await DoctorNameLookup.LoadAsync(doctors, [appointment.DoctorId], ct);
        // Bu randevunun ödemesi (varsa): ödenmiş olan öne alınır. Payment servisi kapalıysa sayfa yine açılır.
        var paymentList = await payments.ListPaymentsAsync(1, 10, id, null, ApiFailures.All, ct);
        var payment = paymentList.Value?.Items.OrderByDescending(p => p.Status == "Paid").FirstOrDefault();

        return View(new AppointmentDetailViewModel
        {
            Appointment = appointment,
            DoctorName = names.NameOf(appointment.DoctorId),
            Payment = payment,
        });
    }

    [HttpGet]
    public async Task<IActionResult> Create(string? doctorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doctorId))
        {
            SetWarning("Önce randevu almak istediğiniz doktoru seçin.");
            return RedirectToAction("Index", "Doctors", new { area = "" });
        }

        var model = new CreateAppointmentViewModel { DoctorId = doctorId };
        await FillDoctorAsync(model, ct);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateAppointmentViewModel model, CancellationToken ct)
    {
        // Şube ve doktor bilgisi formdan değil API'den alınır (şube kimliği istemciden güvenilmez).
        var doctor = await FillDoctorAsync(model, ct);

        DateTime? utc = null;
        if (model.AppointmentLocal is { } local)
        {
            utc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
            if (utc <= DateTime.UtcNow)
                ModelState.AddModelError(nameof(model.AppointmentLocal), "Randevu tarihi gelecekte olmalı.");
        }

        if (!ModelState.IsValid || utc is null) return View(model);

        var result = await appointments.CreateAsync(new CreateAppointmentRequest(doctor.Id, doctor.BranchId, utc.Value), ct);
        if (!result.TryGetValue(out var created))
        {
            // 409: doktorun o saati dolu; 400: tarih geçmişte vb. İkisi de forma döner, girilen tarih korunur.
            ModelState.AddApiProblem(result, "Randevu oluşturulamadı.");
            return View(model);
        }

        SetSuccess("Randevunuz oluşturuldu. Doktor onayladığında durumu güncellenecek.");
        return RedirectToAction(nameof(Details), new { id = created.Id });
    }

    // Hastanın yapabildiği tek durum değişikliği: iptal (API: hasta yalnız Cancelled'a geçirebilir; Pending/Confirmed'dan).
    [HttpPost]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var result = await appointments.SetStatusAsync(id, "Cancelled", ct);
        if (result.IsSuccess) SetSuccess("Randevunuz iptal edildi.");
        else SetWarning(result.Problem?.Detail ?? "Randevu iptal edilemedi.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<DoctorDetailApiModel> FillDoctorAsync(CreateAppointmentViewModel model, CancellationToken ct)
    {
        var result = await doctors.GetAsync(model.DoctorId, ct);
        var doctor = result.Value!;
        model.DoctorName = DisplayText.DoctorName(doctor.Title, doctor.FirstName, doctor.LastName);
        model.Schedule = doctor.ClinicLocations.SelectMany(c => c.Schedule).ToList();
        return doctor;
    }
}
