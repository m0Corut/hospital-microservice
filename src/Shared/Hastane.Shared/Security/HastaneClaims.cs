namespace Hastane.Shared.Security;

// Token içindeki claim adları (JWT-TASARIM.md §3). MapInboundClaims kapalı olduğu için token'daki
// kısa ad neyse kodda da o kullanılır; "http://schemas.xmlsoap.org/..." gibi uzun adlar yok.
public static class HastaneClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string DoctorId = "doctor_id";
    public const string TokenId = "jti";
}
