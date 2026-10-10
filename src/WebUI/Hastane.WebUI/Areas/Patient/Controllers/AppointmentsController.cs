using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Patient;
using Hastane.WebUI.Models.Api.Public;
using Hastane.WebUI.ViewModels.Patient;
using Hastane.WebUI.ViewModels.Public;
using Hastane.WebUI.ViewModels.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Patient.Controllers;

public class AppointmentsController(
    IPatientAppointmentClient appointments, IPatientPaymentClient payments, IPublicDoctorClient doctors,
    IAppointmentRescheduleClient reschedule, IAppointmentAvailabilityClient availability) : PatientAreaController
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
    public async Task<IActionResult> Create(string? doctorId, string? date, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doctorId))
        {
            SetWarning("Önce randevu almak istediğiniz doktoru seçin.");
            return RedirectToAction("Index", "Doctors", new { area = "" });
        }

        var model = new CreateAppointmentViewModel { DoctorId = doctorId, Date = date };
        await FillDoctorAsync(model, ct);
        await FillPickerAsync(model, ct);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateAppointmentViewModel model, CancellationToken ct)
    {
        // Şube ve doktor bilgisi formdan değil API'den alınır (şube kimliği istemciden güvenilmez).
        var doctor = await FillDoctorAsync(model, ct);

        // Saat serbest girilmez: radio'dan gelen slotun UTC başlangıcı. Programa uyum ve çakışma API'de denetlenir (400 / 409).
        var utc = AppointmentTime.ParseSlot(model.SlotStart, ModelState, nameof(model.SlotStart));

        if (ModelState.IsValid && utc is { } start)
        {
            var note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();
            var result = await appointments.CreateAsync(new CreateAppointmentRequest(doctor.Id, doctor.BranchId, start, note), ct);
            if (result.TryGetValue(out var created))
            {
                // "Randevunuz alındı" onay sayfası: özet + "Şimdi öde" + "Randevularım".
                return RedirectToAction(nameof(Confirmation), new { id = created.Id });
            }

            // 400: program dışı saat / geçmiş; 409: saat dolu; 503: doktor servisi kapalı. Hepsi forma döner, gün ve not korunur.
            ModelState.AddBookingProblem(result, "Randevu oluşturulamadı.");
        }

        await FillPickerAsync(model, ct);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Confirmation(Guid id, CancellationToken ct)
    {
        var appointment = (await appointments.GetAsync(id, ct)).Value!;
        var names = await DoctorNameLookup.LoadAsync(doctors, [appointment.DoctorId], ct);
        // Ödeme durumu "Şimdi öde" düğmesi içindir; Payment servisi kapalıysa sayfa yine açılır.
        var paymentList = await payments.ListPaymentsAsync(1, 10, id, null, ApiFailures.All, ct);
        var payment = paymentList.Value?.Items.OrderByDescending(p => p.Status == "Paid").FirstOrDefault();

        return View(new AppointmentDetailViewModel
        {
            Appointment = appointment,
            DoctorName = names.NameOf(appointment.DoctorId),
            Payment = payment,
        });
    }

    // Yeniden planlama: yalnız bekleyen / onaylı randevu. Sahiplik ve son durum kuralı API'dedir (404 / 409).
    [HttpGet]
    public async Task<IActionResult> Reschedule(Guid id, string? date, CancellationToken ct)
    {
        var appointment = (await appointments.GetAsync(id, ct)).Value!;
        if (appointment.Status is not ("Pending" or "Confirmed"))
        {
            SetWarning("Yalnız bekleyen ya da onaylanmış randevu yeniden planlanabilir.");
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(await BuildRescheduleAsync(appointment, new RescheduleViewModel { Id = id, Date = date }, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Reschedule(Guid id, RescheduleViewModel model, CancellationToken ct)
    {
        // Şube, doktor ve durum formdan değil API'den alınır.
        var appointment = (await appointments.GetAsync(id, ct)).Value!;
        model.Id = id;
        var utc = AppointmentTime.ParseSlot(model.SlotStart, ModelState, nameof(model.SlotStart));

        if (ModelState.IsValid && utc is { } newDate)
        {
            var result = await reschedule.RescheduleAsync(id, appointment.BranchId, newDate, ct);
            if (result.IsSuccess)
            {
                SetSuccess("Randevunuz yeniden planlandı.");
                return RedirectToAction(nameof(Details), new { id });
            }

            // 400: program dışı saat; 409: o saat dolu ya da randevu artık değiştirilemez; 503: doktor servisi kapalı.
            ModelState.AddBookingProblem(result, "Randevu yeniden planlanamadı.");
        }

        return View(await BuildRescheduleAsync(appointment, model, ct));
    }

    private async Task<RescheduleViewModel> BuildRescheduleAsync(AppointmentDetailApiModel appointment, RescheduleViewModel model, CancellationToken ct)
    {
        var names = await DoctorNameLookup.LoadAsync(doctors, [appointment.DoctorId], ct);
        model.PartyLabel = names.NameOf(appointment.DoctorId);
        model.CurrentDate = appointment.AppointmentDate;
        model.Picker = await AppointmentTime.BuildPickerAsync(availability, appointment.DoctorId, model.Date,
            day => Url.Action(nameof(Reschedule), new { id = model.Id, date = AppointmentTime.DateText(day) })!, ct);
        model.Picker.SelectedStart = model.SlotStart;
        return model;
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

    private async Task FillPickerAsync(CreateAppointmentViewModel model, CancellationToken ct)
    {
        model.Picker = await AppointmentTime.BuildPickerAsync(availability, model.DoctorId, model.Date,
            day => Url.Action(nameof(Create), new { doctorId = model.DoctorId, date = AppointmentTime.DateText(day) })!, ct);
        model.Picker.SelectedStart = model.SlotStart;
    }

    private async Task<DoctorDetailApiModel> FillDoctorAsync(CreateAppointmentViewModel model, CancellationToken ct)
    {
        var result = await doctors.GetAsync(model.DoctorId, ct);
        var doctor = result.Value!;
        model.DoctorName = DisplayText.DoctorName(doctor.Title, doctor.FirstName, doctor.LastName);
        model.DoctorImageUrl = doctor.ImageUrl;
        model.DoctorDegrees = doctor.Degrees;
        model.DoctorCity = doctor.PrimaryCity;
        model.FeeText = DisplayText.FeeRange(doctor.MinFee, doctor.MaxFee, doctor.Currency);
        model.Schedule = doctor.ClinicLocations.SelectMany(c => c.Schedule).ToList();
        return doctor;
    }
}
