using System.Text.RegularExpressions;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Patient;
using Hastane.WebUI.ViewModels.Patient;
using Hastane.WebUI.ViewModels.Public;
using Microsoft.AspNetCore.Mvc;

namespace Hastane.WebUI.Areas.Patient.Controllers;

public partial class PaymentsController(
    IPatientPaymentClient payments, IPatientAppointmentClient appointments, IPublicDoctorClient doctors) : PatientAreaController
{
    private const int PageSize = 10;

    public async Task<IActionResult> Index(int page = 1, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var result = await payments.ListPaymentsAsync(page, PageSize, null, null, ApiFailures.None, ct);
        var list = result.Value!;
        var names = await DoctorNameLookup.LoadAsync(doctors, list.Items.Select(p => p.DoctorId), ct);

        return View(new PaymentListViewModel
        {
            Page = page,
            TotalPages = Math.Max(1, (list.TotalCount + PageSize - 1) / PageSize),
            Items = list.Items.Select(p => new PaymentRowViewModel { Payment = p, DoctorName = names.NameOf(p.DoctorId) }).ToList(),
        });
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var result = await payments.GetPaymentAsync(id, ct);
        var payment = result.Value!;
        var names = await DoctorNameLookup.LoadAsync(doctors, [payment.DoctorId], ct);
        return View(new PaymentDetailViewModel { Payment = payment, DoctorName = names.NameOf(payment.DoctorId) });
    }

    // Kart sayfaları önbelleğe alınmaz (geri tuşu / paylaşılan bilgisayar).
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Create(Guid appointmentId, CancellationToken ct)
    {
        var context = await LoadContextAsync(appointmentId, ct);
        if (context.Redirect is not null) return context.Redirect;

        // Anahtar form her açıldığında BİR kez üretilir, gizli alanla taşınır: çift tıklama/yeniden gönderim aynı anahtarı yollar.
        var model = context.Fill(new CreatePaymentViewModel { AppointmentId = appointmentId, IdempotencyKey = NewKey() });
        return View(model);
    }

    [HttpPost, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Create(CreatePaymentViewModel model, CancellationToken ct)
    {
        // Tutar, doktor ve randevu bilgisi formdan alınmaz: API'den yeniden okunur (formu kurcalayıp tutarı değiştirmek işe yaramaz).
        var context = await LoadContextAsync(model.AppointmentId, ct);
        if (context.Redirect is not null) return context.Redirect;

        var number = SeparatorChars().Replace(model.CardNumber ?? string.Empty, string.Empty);
        var cvv = (model.Cvv ?? string.Empty).Trim();
        var holder = (model.HolderName ?? string.Empty).Trim();

        if (!IdempotencyKeyFormat().IsMatch(model.IdempotencyKey ?? string.Empty))
            model.IdempotencyKey = NewKey(); // bozuk/eksik anahtar: yeni niyet
        if (ModelState.GetValueOrDefault(nameof(model.CardNumber))?.Errors.Count is null or 0 && !CardNumberFormat().IsMatch(number))
            ModelState.AddModelError(nameof(model.CardNumber), "Kart numarası 12-19 haneli rakam olmalı.");
        if (ModelState.GetValueOrDefault(nameof(model.Cvv))?.Errors.Count is null or 0 && !CvvFormat().IsMatch(cvv))
            ModelState.AddModelError(nameof(model.Cvv), "CVV 3 veya 4 haneli olmalı.");

        if (!ModelState.IsValid) return View(Scrub(context.Fill(model)));

        var request = new CreatePaymentRequest(
            model.AppointmentId, context.Appointment!.DoctorId, context.Amount, context.Currency, "Randevu ücreti",
            new PaymentCardRequest(number, model.ExpMonth!.Value, model.ExpYear!.Value, cvv, holder));

        // Servis erişilemezse (502/zaman aşımı) 503 sayfası yerine forma dönülür: sonuç belirsiz, AYNI anahtarla tekrar denenebilir.
        var result = await payments.CreatePaymentAsync(model.IdempotencyKey!, request, ApiFailures.Unavailable, ct);

        if (result.TryGetValue(out var paid))
        {
            SetSuccess("Ödemeniz alındı. Faturanız oluşturuldu.");
            return RedirectToAction(nameof(Details), new { id = paid.Id });
        }

        switch (result.Kind)
        {
            case ApiResultKind.ValidationFailed:
                // Sunucu isteği kabul etmeden reddetti (anahtar rezerve edilmedi): aynı anahtar kalır.
                ModelState.AddApiProblem(result, "Ödeme bilgileri geçersiz.");
                break;
            case ApiResultKind.Conflict:
                // Aynı randevu zaten ödenmiş / işlem sürüyor / anahtar başka içerikle kullanılmış. Anahtarı DEĞİŞTİRME:
                // yeni anahtar, süren bir işlemin ikinci kez tahsil edilmesi demek olabilir. Sayfayı yenilemek yeni niyet başlatır.
                ModelState.AddApiProblem(result, "Ödeme tamamlanamadı.");
                break;
            case ApiResultKind.Unavailable:
                ModelState.AddModelError(string.Empty,
                    "Ödeme şu anda işlenemedi. Aynı bilgilerle tekrar deneyebilirsiniz; aynı işlem iki kez tahsil edilmez.");
                break;
            default:
                if (result.StatusCode == StatusCodes.Status402PaymentRequired)
                {
                    // Sağlayıcı reddetti, para çekilmedi. Bu niyet kapandı; yeni deneme yeni anahtar ister.
                    ModelState.AddModelError(string.Empty, "Kartınız ödeme sağlayıcısı tarafından reddedildi. Kart bilgilerini ya da bakiyenizi kontrol edip yeniden deneyin.");
                    model.IdempotencyKey = NewKey();
                }
                else
                {
                    ModelState.AddApiProblem(result, "Ödeme tamamlanamadı.");
                }

                break;
        }

        return View(Scrub(context.Fill(model)));
    }

    // Kart numarası ve CVV forma geri yazılmaz, hiçbir yere saklanmaz.
    private static CreatePaymentViewModel Scrub(CreatePaymentViewModel model)
    {
        model.CardNumber = string.Empty;
        model.Cvv = string.Empty;
        model.ExpMonth = null;
        model.ExpYear = null;
        return model;
    }

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private sealed class PaymentContext
    {
        public IActionResult? Redirect { get; init; }
        public AppointmentDetailApiModel? Appointment { get; init; }
        public string DoctorName { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public string Currency { get; init; } = string.Empty;

        public CreatePaymentViewModel Fill(CreatePaymentViewModel model)
        {
            model.DoctorName = DoctorName;
            model.Amount = Amount;
            model.Currency = Currency;
            model.AppointmentDate = Appointment!.AppointmentDate;
            return model;
        }
    }

    private async Task<PaymentContext> LoadContextAsync(Guid appointmentId, CancellationToken ct)
    {
        // Başkasının randevusu / yok: API 404 verir, 404 sayfası açılır.
        var appointment = (await appointments.GetAsync(appointmentId, ct)).Value!;

        if (appointment.Status == "Cancelled")
        {
            SetWarning("İptal edilmiş randevu için ödeme yapılamaz.");
            return new PaymentContext { Redirect = RedirectToAction("Details", "Appointments", new { id = appointmentId }) };
        }

        var paid = await payments.ListPaymentsAsync(1, 1, appointmentId, "Paid", ApiFailures.All, ct);
        if (paid.Value?.Items.FirstOrDefault() is { } existing)
        {
            SetWarning("Bu randevunun ödemesi zaten yapılmış.");
            return new PaymentContext { Redirect = RedirectToAction(nameof(Details), new { id = existing.Id }) };
        }

        var doctor = (await doctors.GetAsync(appointment.DoctorId, ct)).Value!;
        if (doctor.MinFee is not { } fee || fee <= 0)
        {
            SetWarning("Bu doktor için muayene ücreti tanımlı değil; çevrimiçi ödeme yapılamıyor.");
            return new PaymentContext { Redirect = RedirectToAction("Details", "Appointments", new { id = appointmentId }) };
        }

        return new PaymentContext
        {
            Appointment = appointment,
            Amount = fee,
            Currency = string.IsNullOrWhiteSpace(doctor.Currency) ? "TRY" : doctor.Currency,
            DoctorName = DisplayText.DoctorName(doctor.Title, doctor.FirstName, doctor.LastName),
        };
    }

    [GeneratedRegex(@"[\s-]")]
    private static partial Regex SeparatorChars();

    [GeneratedRegex(@"^\d{12,19}$")]
    private static partial Regex CardNumberFormat();

    [GeneratedRegex(@"^\d{3,4}$")]
    private static partial Regex CvvFormat();

    [GeneratedRegex("^[A-Za-z0-9_-]{16,100}$")]
    private static partial Regex IdempotencyKeyFormat();
}
