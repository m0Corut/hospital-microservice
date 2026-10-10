using System.ComponentModel.DataAnnotations;
using Hastane.WebUI.Models.Api.Patient;
using Hastane.WebUI.Models.Api.Public;
using Hastane.WebUI.ViewModels.Shared;

namespace Hastane.WebUI.ViewModels.Patient;

public sealed class AppointmentRowViewModel
{
    public required AppointmentApiModel Appointment { get; init; }
    public required string DoctorName { get; init; }
}

public sealed class AppointmentListViewModel
{
    public List<AppointmentRowViewModel> Items { get; init; } = [];
    public string? Status { get; init; }
}

public sealed class AppointmentDetailViewModel
{
    public required AppointmentDetailApiModel Appointment { get; init; }
    public required string DoctorName { get; init; }
    public PaymentApiModel? Payment { get; init; }
    public bool CanCancel => Appointment.Status is "Pending" or "Confirmed";
    public bool CanPay => Appointment.Status != "Cancelled" && Payment is not { Status: "Paid" };
    public bool CanReschedule => Appointment.Status is "Pending" or "Confirmed";
}

public sealed class CreateAppointmentViewModel
{
    [Required(ErrorMessage = "Doktor seçilmedi.")]
    public string DoctorId { get; set; } = string.Empty;

    // Seçilen slotun UTC başlangıcı ("yyyy-MM-ddTHH:mm:ssZ"): saat seçicinin radio'sundan gelir, serbest tarih girişi yok.
    [Required(ErrorMessage = "Bir saat seçin.")]
    [Display(Name = "Saat")]
    public string? SlotStart { get; set; }

    // Gösterilen gün (yyyy-MM-dd); hatayla dönüşte aynı gün yeniden çizilir.
    public string? Date { get; set; }

    [Display(Name = "Şikâyet / not")]
    [StringLength(500, ErrorMessage = "Not en çok 500 karakter olabilir.")]
    public string? Note { get; set; }

    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public SlotPickerViewModel Picker { get; set; } = new();

    // Yalnız gösterim için; POST'ta yeniden doldurulur (formdan alınmaz).
    public string DoctorName { get; set; } = string.Empty;
    public string? DoctorImageUrl { get; set; }
    public string? DoctorDegrees { get; set; }
    public string? DoctorCity { get; set; }
    public string FeeText { get; set; } = string.Empty;
    public List<ScheduleSlotApiModel> Schedule { get; set; } = [];
}

public sealed class PrescriptionRowViewModel
{
    public required PrescriptionApiModel Prescription { get; init; }
    public required string DoctorName { get; init; }
}

public sealed class PrescriptionDetailViewModel
{
    public required PrescriptionDetailApiModel Prescription { get; init; }
    public required string DoctorName { get; init; }
}

public sealed class PaymentRowViewModel
{
    public required PaymentApiModel Payment { get; init; }
    public required string DoctorName { get; init; }
}

public sealed class PaymentListViewModel
{
    public List<PaymentRowViewModel> Items { get; init; } = [];
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
}

public sealed class PaymentDetailViewModel
{
    public required PaymentApiModel Payment { get; init; }
    public required string DoctorName { get; init; }
}

// Ödeme formu. Kart numarası ve CVV forma GERİ YAZILMAZ: başarısızlıkta alanlar boşaltılır, görünüm değer basmaz.
public sealed class CreatePaymentViewModel
{
    [Required]
    public Guid AppointmentId { get; set; }

    // Form açılırken sunucu üretir, gizli alanla taşınır. Çift tıklamada aynı anahtar gider; sunucu ikinci kez kartı çekmez.
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kart numarası gerekli.")]
    [Display(Name = "Kart numarası")]
    public string CardNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ay gerekli."), Range(1, 12, ErrorMessage = "Ay 1 ile 12 arasında olmalı.")]
    [Display(Name = "Son kullanma ayı")]
    public int? ExpMonth { get; set; }

    [Required(ErrorMessage = "Yıl gerekli."), Range(2000, 2200, ErrorMessage = "Geçerli bir yıl girin.")]
    [Display(Name = "Son kullanma yılı")]
    public int? ExpYear { get; set; }

    [Required(ErrorMessage = "CVV gerekli.")]
    [Display(Name = "CVV")]
    public string Cvv { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kart üzerindeki ad gerekli."), StringLength(100)]
    [Display(Name = "Kart üzerindeki ad")]
    public string HolderName { get; set; } = string.Empty;

    // Gösterim (formdan alınmaz): tutar doktorun profilinden sunucuda hesaplanır.
    public string DoctorName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
}

public sealed class InvoiceRowViewModel
{
    public required InvoiceApiModel Invoice { get; init; }
    public required string DoctorName { get; init; }
}

public sealed class InvoiceListViewModel
{
    public List<InvoiceRowViewModel> Items { get; init; } = [];
    public int Page { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
}

public sealed class InvoiceDetailViewModel
{
    public required InvoiceDetailApiModel Invoice { get; init; }
    public required string DoctorName { get; init; }
}

public sealed class PatientHomeViewModel
{
    public List<AppointmentRowViewModel> Upcoming { get; init; } = [];
    public List<PrescriptionRowViewModel> Prescriptions { get; init; } = [];
    public bool AppointmentsUnavailable { get; init; }
    public bool PrescriptionsUnavailable { get; init; }

    // Özet kutuları: listeler panelde kısaltılır (ilk 5), sayılar kısaltılmamış listeden gelir.
    public int UpcomingCount { get; init; }
    public int PrescriptionCount { get; init; }
}
