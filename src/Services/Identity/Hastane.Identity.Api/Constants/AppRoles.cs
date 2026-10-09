namespace Hastane.Identity.Api.Constants;

// Rol adları tek yerde: yazım hatası ("Pasient") sessizce başka rol yaratmasın diye string'ler dağılmaz.
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Doctor = "Doctor";
    public const string Patient = "Patient";

    public static readonly string[] All = [Admin, Doctor, Patient];
}
