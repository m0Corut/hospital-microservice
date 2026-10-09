namespace Hastane.WebUI.Models.Api;

// Identity servisinin DTO'larının WebUI'daki kendi kopyaları (servis projesine başvuru yok; sözleşme JSON'dur).
public sealed class LoginApiResult
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public DateTime ExpiresAt { get; set; }
    public UserSummaryApiModel User { get; set; } = new();
}

public sealed class UserSummaryApiModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public string? DoctorId { get; set; }
}
