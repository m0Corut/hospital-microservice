using Hastane.Appointment.Api.Entities;
using Hastane.Shared.Security;
using AppointmentEntity = Hastane.Appointment.Api.Entities.Appointment;

namespace Hastane.Appointment.Api.Services;

// "Bu kullanıcı bu randevunun sahibi mi?" kuralı tek yerde. Rol TEK BAŞINA yetmez: Doctor rolü olan biri
// yalnız doctor_id'si randevudaki DoctorId ile eşleşiyorsa o randevunun doktorudur; hasta için sub == PatientId.
public static class AppointmentAccess
{
    public static bool IsPatientOf(ICurrentUser user, AppointmentEntity a)
        => user.IsInRole(HastaneRoles.Patient) && user.UserId is not null && a.PatientId == user.UserId;

    public static bool IsDoctorOf(ICurrentUser user, AppointmentEntity a)
        => user.IsInRole(HastaneRoles.Doctor) && user.DoctorId is not null && a.DoctorId == user.DoctorId;

    // Görme / yeniden planlama: hasta, doktor ya da Admin. Başkası için servis 404 döner (varlık sızmasın).
    public static bool CanAccess(ICurrentUser user, AppointmentEntity a)
        => user.IsAdmin || IsPatientOf(user, a) || IsDoctorOf(user, a);

    // Alan düzeyinde yetki: aynı uç (PATCH status), ama hangi HEDEF duruma kim geçirebilir.
    public static bool CanSetStatus(ICurrentUser user, AppointmentEntity a, AppointmentStatus target)
    {
        if (user.IsAdmin) return true;
        if (IsDoctorOf(user, a)
            && target is AppointmentStatus.Confirmed or AppointmentStatus.Completed or AppointmentStatus.Cancelled)
            return true;
        // Hasta yalnız kendi randevusunu iptal edebilir; onaylayamaz, tamamlayamaz.
        return IsPatientOf(user, a) && target == AppointmentStatus.Cancelled;
    }
}
