using System.ComponentModel.DataAnnotations;
using Hastane.WebUI.Models.Api.DoctorPanel;

namespace Hastane.WebUI.ViewModels.DoctorPanel;

public sealed class DoctorHomeViewModel
{
    public List<AppointmentRow> Today { get; set; } = [];
    public List<AppointmentRow> Upcoming { get; set; } = [];
    public List<AppointmentRow> Pending { get; set; } = [];

    // Özet kutuları: panelde listeler kısaltılır (8), sayılar kısaltılmamış listelerden gelir.
    public int UpcomingCount { get; set; }
    public int PendingCount { get; set; }
}

public sealed class AppointmentFilterViewModel
{
    [Display(Name = "Durum")]
    public DoctorPanelAppointmentStatus? Status { get; set; }

    [DataType(DataType.Date), Display(Name = "Başlangıç tarihi")]
    public DateTime? From { get; set; }

    [DataType(DataType.Date), Display(Name = "Bitiş tarihi")]
    public DateTime? To { get; set; }
}

public sealed class AppointmentListViewModel
{
    public AppointmentFilterViewModel Filter { get; set; } = new();
    public List<AppointmentRow> Rows { get; set; } = [];
}

public sealed class AppointmentDetailForm
{
    [Required(ErrorMessage = "Şikayet gerekli."), StringLength(1000, ErrorMessage = "Şikayet en çok 1000 karakter olabilir.")]
    [Display(Name = "Şikayet")]
    public string Complaint { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Tanı en çok 1000 karakter olabilir.")]
    [Display(Name = "Tanı")]
    public string? Diagnosis { get; set; }

    [StringLength(2000, ErrorMessage = "Doktor notu en çok 2000 karakter olabilir.")]
    [Display(Name = "Doktor notu")]
    public string? DoctorNotes { get; set; }
}

public sealed class PrescriptionLinkRow
{
    public Guid Id { get; set; }
    public DateTime IssuedAt { get; set; }
    public int ItemCount { get; set; }
}

public sealed class AppointmentDetailViewModel
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public DoctorPanelAppointmentStatus Status { get; set; }
    public string PatientLabel { get; set; } = string.Empty;
    public string BranchLabel { get; set; } = string.Empty;
    // Hastanın randevu alırken yazdığı şikâyet / not.
    public string? Note { get; set; }
    public AppointmentDetailForm Form { get; set; } = new();
    public DateTime? DetailUpdatedAt { get; set; }
    public List<PrescriptionLinkRow> Prescriptions { get; set; } = [];
}

public sealed class PrescriptionListRow
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public DateTime IssuedAt { get; set; }
    public string PatientLabel { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}

public sealed class PrescriptionDetailViewModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public DateTime IssuedAt { get; set; }
    public string PatientLabel { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<DoctorPanelPrescriptionItemApiModel> Items { get; set; } = [];
}

public sealed class PrescriptionItemForm
{
    [Required(ErrorMessage = "İlaç adı gerekli."), StringLength(200, ErrorMessage = "İlaç adı en çok 200 karakter olabilir.")]
    [Display(Name = "İlaç adı")]
    public string MedicineName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Doz gerekli."), StringLength(100, ErrorMessage = "Doz en çok 100 karakter olabilir.")]
    [Display(Name = "Doz")]
    public string Dosage { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kullanım sıklığı gerekli."), StringLength(100, ErrorMessage = "Sıklık en çok 100 karakter olabilir.")]
    [Display(Name = "Sıklık")]
    public string Frequency { get; set; } = string.Empty;

    [Range(1, 365, ErrorMessage = "Süre 1 ile 365 gün arasında olmalı.")]
    [Display(Name = "Süre (gün)")]
    public int DurationDays { get; set; } = 7;

    [StringLength(500, ErrorMessage = "Kullanım notu en çok 500 karakter olabilir.")]
    [Display(Name = "Kullanım notu")]
    public string? Instructions { get; set; }
}

public sealed class PrescriptionFormViewModel
{
    public const int MaxItems = 20;

    // Düzenlemede dolu, yeni reçetede boş.
    public Guid? PrescriptionId { get; set; }
    public Guid AppointmentId { get; set; }

    // Yalnız gösterim (formdan gelen değere güvenilmez; her istekte API'den yeniden okunur).
    public string PatientLabel { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Not en çok 2000 karakter olabilir.")]
    [Display(Name = "Reçete notu")]
    public string? Notes { get; set; }

    public List<PrescriptionItemForm> Items { get; set; } = [];
}

public sealed class ReviewsViewModel
{
    public int ReviewCount { get; set; }
    public double AverageRating { get; set; }
    public int RecommendationPercent { get; set; }
    public List<DoctorPanelReviewApiModel> Reviews { get; set; } = [];
}

public sealed record PatientRow(string Label, int AppointmentCount, DateTime LastDate, Guid LastAppointmentId);

public sealed record CalendarEvent(Guid Id, DateTime Date, string PatientLabel, DoctorPanelAppointmentStatus Status);

public sealed record CalendarDay(DateTime Date, bool IsToday, List<CalendarEvent> Events);

// Haftalık takvim: sunucuda çizilen 7 sütunlu ızgara (JS kütüphanesi yok). Hafta Pazartesi başlar.
public sealed class CalendarViewModel
{
    public DateTime WeekStart { get; init; }
    public List<CalendarDay> Days { get; init; } = [];
    public DateTime Previous => WeekStart.AddDays(-7);
    public DateTime Next => WeekStart.AddDays(7);
    public int EventCount => Days.Sum(d => d.Events.Count);
}
