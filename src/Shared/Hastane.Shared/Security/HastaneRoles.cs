namespace Hastane.Shared.Security;

// Rol adları tek yerde: "Pasient" gibi yazım hatası sessizce başka rol yaratmasın.
public static class HastaneRoles
{
    public const string Admin = "Admin";
    public const string Doctor = "Doctor";
    public const string Patient = "Patient";

    public static readonly string[] All = [Admin, Doctor, Patient];
}
