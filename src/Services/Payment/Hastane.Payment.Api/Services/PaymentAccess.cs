using Hastane.Shared.Security;

namespace Hastane.Payment.Api.Services;

public enum AccessScope
{
    All,      // Admin: kısıt yok
    Patient,  // yalnız PatientId == sub
    Doctor,   // yalnız DoctorId == doctor_id
    Either,   // hem Patient hem (bağlı) Doctor rolü: ikisinden biri
    None,     // doctor_id'siz Doctor ya da rolsüz: hiçbir şey
}

// "Bu kullanıcı bu ödemeyi/faturayı görebilir mi?" kuralı tek yerde (ödeme ve fatura aynı kuralı paylaşır).
// Rol TEK BAŞINA yetmez: Doctor rolü olan biri yalnız doctor_id'si kaydın DoctorId'siyle eşleşiyorsa görür.
public static class PaymentAccess
{
    public static AccessScope ScopeOf(ICurrentUser user)
    {
        if (user.IsAdmin) return AccessScope.All;
        var isPatient = user.IsInRole(HastaneRoles.Patient) && user.UserId is not null;
        var isDoctor = user.IsInRole(HastaneRoles.Doctor) && user.DoctorId is not null;
        return (isPatient, isDoctor) switch
        {
            (true, true) => AccessScope.Either,
            (true, false) => AccessScope.Patient,
            (false, true) => AccessScope.Doctor,
            _ => AccessScope.None,
        };
    }

    public static bool CanView(ICurrentUser user, string patientId, string doctorId) => ScopeOf(user) switch
    {
        AccessScope.All => true,
        AccessScope.Patient => patientId == user.UserId,
        AccessScope.Doctor => doctorId == user.DoctorId,
        AccessScope.Either => patientId == user.UserId || doctorId == user.DoctorId,
        _ => false,
    };
}
