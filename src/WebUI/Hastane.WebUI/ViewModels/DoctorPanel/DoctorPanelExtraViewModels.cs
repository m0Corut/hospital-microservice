using Hastane.WebUI.Models.Api.DoctorPanel;
using Hastane.WebUI.Models.Api.Patient;

namespace Hastane.WebUI.ViewModels.DoctorPanel;

// ---------- Çalışma Saatleri ----------

// Bir klinik konumunun haftalık programı. ClinicName yalnız gösterim + kayıt anında "liste kaymadı" kontrolü içindir;
// kaydedilen klinik bilgileri (ad, adres, ücret...) her zaman API'den yeniden okunan kayıttan gelir.
public sealed class ScheduleClinicForm
{
    public string ClinicName { get; set; } = string.Empty;
    public List<ScheduleSlotForm> Schedule { get; set; } = [];
}

// Alan adı ClinicLocations: API'nin 400 anahtarı ("ClinicLocations[0].Schedule") doğrudan forma oturur.
public sealed class ScheduleFormViewModel
{
    public const int MaxSlotsPerClinic = 21;

    public List<ScheduleClinicForm> ClinicLocations { get; set; } = [];
}

// ---------- Sosyal Medya ----------

public sealed class SocialFormViewModel
{
    public SocialLinksForm SocialLinks { get; set; } = new();
}

// ---------- Faturalarım ----------

public sealed class DoctorInvoiceRow
{
    public required InvoiceApiModel Invoice { get; init; }
    public string PatientLabel { get; init; } = string.Empty;
    public bool Refunded { get; init; }
}

// Para birimi başına tutar (karışık para birimi toplanmaz).
public sealed record MoneyTotal(string Currency, decimal Amount);

public sealed class DoctorEarningsSummary
{
    // Yalnız TÜM ödeme sayfaları alınabildiyse dolu (kısmi veriyle tahmin gösterilmez).
    public int PaidCount { get; init; }
    public IReadOnlyList<MoneyTotal> ThisMonth { get; init; } = [];
    public IReadOnlyList<MoneyTotal> Total { get; init; } = [];
    public int RefundedCount { get; init; }
}

public sealed class DoctorInvoiceListViewModel
{
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public List<DoctorInvoiceRow> Items { get; init; } = [];

    // null: özet hesaplanamadı (servis yanıtsız ya da kayıt sayısı sınırı aştı).
    public DoctorEarningsSummary? Summary { get; init; }
    public string? SummaryNote { get; init; }
}

public sealed class DoctorInvoiceDetailViewModel
{
    public required InvoiceDetailApiModel Invoice { get; init; }
    public string PatientLabel { get; init; } = string.Empty;
    public bool Refunded { get; init; }
}

// ---------- Hasta Profili ----------

public sealed class PatientAppointmentRow
{
    public Guid Id { get; init; }
    public DateTime Date { get; init; }
    public string BranchLabel { get; init; } = string.Empty;
    public DoctorPanelAppointmentStatus Status { get; init; }
    public string? Note { get; init; }
}

public sealed class PatientProfileViewModel
{
    public string PatientId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public List<PatientAppointmentRow> Appointments { get; init; } = [];
    public List<PrescriptionListRow> Prescriptions { get; init; } = [];
    public int CompletedCount { get; init; }
    public DateTime? FirstVisit { get; init; }
    public DateTime? LastVisit { get; init; }
}
