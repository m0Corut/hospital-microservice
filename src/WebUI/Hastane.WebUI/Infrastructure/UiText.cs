using Hastane.Shared.Security;

namespace Hastane.WebUI.Infrastructure;

// Arayüz metinleri Türkçe; kodda ve veride rol adları İngilizce kalır.
public static class UiText
{
    public static string RoleName(string role) => role switch
    {
        HastaneRoles.Admin => "Yönetici",
        HastaneRoles.Doctor => "Doktor",
        HastaneRoles.Patient => "Hasta",
        _ => role,
    };
}
