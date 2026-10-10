namespace Hastane.WebUI.Models.Api.Admin;

// Identity servisi: GET /gateway/users/{id} ve rol uçlarının döndürdüğü kullanıcı özeti.
public sealed class AdminUserApiModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public string? DoctorId { get; set; }
}

public sealed class AdminRoleApiModel
{
    public string Name { get; set; } = string.Empty;
}
