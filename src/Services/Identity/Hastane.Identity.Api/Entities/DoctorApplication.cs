namespace Hastane.Identity.Api.Entities;

public enum DoctorApplicationStatus { Pending = 0, Approved = 1, Rejected = 2 }

// Doktor olmak isteyen kullanıcının başvurusu. Kullanıcı başvuru anında Patient'tır; Admin onaylarsa Doctor olur.
// Bir kullanıcının en çok bir başvurusu olur (UserId benzersiz).
public class DoctorApplication
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    // Doctor servisindeki branş kimliği (metin; Doctor servisine sorulmaz). İsteğe bağlı.
    public string? BranchId { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DoctorApplicationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    // Kararı veren Admin'in kullanıcı kimliği (token'dan).
    public string? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }
}
