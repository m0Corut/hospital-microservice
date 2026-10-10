using Hastane.WebUI.Models.Api.DoctorPanel;

namespace Hastane.WebUI.ViewModels.DoctorPanel;

// Doktor paneli ekranlarının ortak biçimleme yardımcıları (tarih, durum, kimlik kısaltma).
public static class DoctorPanelFormat
{
    // API UTC döner (EF bazen Kind'siz verir): Kind belirsizse UTC kabul edip yerel saate çevir.
    public static DateTime ToLocal(DateTime value)
        => value.Kind == DateTimeKind.Local ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime();

    public static string DateTimeText(DateTime value) => ToLocal(value).ToString("dd.MM.yyyy HH:mm");

    public static string ShortId(string id) => id.Length <= 8 ? id : id[..8];

    public static string StatusText(DoctorPanelAppointmentStatus status) => status switch
    {
        DoctorPanelAppointmentStatus.Pending => "Onay bekliyor",
        DoctorPanelAppointmentStatus.Confirmed => "Onaylandı",
        DoctorPanelAppointmentStatus.Completed => "Tamamlandı",
        DoctorPanelAppointmentStatus.Cancelled => "İptal edildi",
        _ => status.ToString(),
    };

    public static string StatusBadge(DoctorPanelAppointmentStatus status) => status switch
    {
        DoctorPanelAppointmentStatus.Pending => "text-bg-warning",
        DoctorPanelAppointmentStatus.Confirmed => "text-bg-primary",
        DoctorPanelAppointmentStatus.Completed => "text-bg-success",
        _ => "text-bg-secondary",
    };

    // Backend durum makinesi (AppointmentStatusTransitions) + doktora izinli hedefler (AppointmentAccess.CanSetStatus):
    // Pending -> Confirmed / Cancelled, Confirmed -> Completed / Cancelled; Completed ve Cancelled son durumdur.
    public static IReadOnlyList<DoctorPanelAppointmentStatus> NextStatuses(DoctorPanelAppointmentStatus current) => current switch
    {
        DoctorPanelAppointmentStatus.Pending => [DoctorPanelAppointmentStatus.Confirmed, DoctorPanelAppointmentStatus.Cancelled],
        DoctorPanelAppointmentStatus.Confirmed => [DoctorPanelAppointmentStatus.Completed, DoctorPanelAppointmentStatus.Cancelled],
        _ => [],
    };

    public static string ActionText(DoctorPanelAppointmentStatus target) => target switch
    {
        DoctorPanelAppointmentStatus.Confirmed => "Onayla",
        DoctorPanelAppointmentStatus.Completed => "Tamamla",
        DoctorPanelAppointmentStatus.Cancelled => "İptal et",
        _ => target.ToString(),
    };

    // "Reçete yaz" yalnız muayenesi onaylanmış ya da bitmiş randevu için anlamlı (arayüz kararı; backend zorlamaz).
    public static bool CanWritePrescription(DoctorPanelAppointmentStatus status)
        => status is DoctorPanelAppointmentStatus.Confirmed or DoctorPanelAppointmentStatus.Completed;

    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Pazartesi",
        DayOfWeek.Tuesday => "Salı",
        DayOfWeek.Wednesday => "Çarşamba",
        DayOfWeek.Thursday => "Perşembe",
        DayOfWeek.Friday => "Cuma",
        DayOfWeek.Saturday => "Cumartesi",
        _ => "Pazar",
    };

    public static string GenderText(DoctorPanelGender gender) => gender switch
    {
        DoctorPanelGender.Male => "Erkek",
        DoctorPanelGender.Female => "Kadın",
        _ => "Diğer",
    };
}

// Randevu satırı: hasta/şube adları controller'da toplu çözülür, görünüm yalnız gösterir.
public sealed record AppointmentRow(
    Guid Id,
    DateTime Date,
    string PatientLabel,
    string BranchLabel,
    DoctorPanelAppointmentStatus Status);
