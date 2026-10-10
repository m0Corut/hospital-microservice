using System.ComponentModel.DataAnnotations;

namespace Hastane.Identity.Api.Dtos;

// Bilerek Role / Status / Id YOK: başvuran yalnız bilgi gönderir, kendini doktor yapamaz.
public class RegisterDoctorDto
{
    [Required, StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;

    [StringLength(64)]
    public string? BranchId { get; set; }

    [Required, StringLength(50)]
    public string LicenseNumber { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Note { get; set; }
}

public class RejectDoctorApplicationDto
{
    [Required, StringLength(500)]
    public string Note { get; set; } = string.Empty;
}

public class DoctorApplicationDto
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
    public string? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }
}
