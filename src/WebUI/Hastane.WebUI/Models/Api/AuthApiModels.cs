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
    // Göreli avatar adresi (/gateway/users/{id}/photo); fotoğraf yoksa null. Tarayıcıya /media/users/{id}/photo olarak sunulur.
    public string? PhotoUrl { get; set; }
}

// GET /gateway/auth/me/doctor-application ve /gateway/doctor-applications satırı. Status: Pending | Approved | Rejected.
public sealed class DoctorApplicationApiModel
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? BranchId { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}
