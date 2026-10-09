using Hastane.Appointment.Api.Entities;

namespace Hastane.Appointment.Api.Services;

// Durum makinesi: hangi durumdan hangisine geçilebilir. Tablo tek yerde, okunur durumda.
public static class AppointmentStatusTransitions
{
    private static readonly Dictionary<AppointmentStatus, AppointmentStatus[]> Allowed = new()
    {
        [AppointmentStatus.Pending] = [AppointmentStatus.Confirmed, AppointmentStatus.Cancelled],
        [AppointmentStatus.Confirmed] = [AppointmentStatus.Completed, AppointmentStatus.Cancelled],
        [AppointmentStatus.Completed] = [],
        [AppointmentStatus.Cancelled] = [],
    };

    public static bool CanTransition(AppointmentStatus from, AppointmentStatus to)
        => Allowed[from].Contains(to);

    // Tamamlanmış ya da iptal edilmiş randevu artık değişmez (yeniden planlanamaz).
    public static bool IsFinal(AppointmentStatus status) => Allowed[status].Length == 0;
}
