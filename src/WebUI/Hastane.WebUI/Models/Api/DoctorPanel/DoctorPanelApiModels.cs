namespace Hastane.WebUI.Models.Api.DoctorPanel;

// Doktor paneli için servis DTO'larının WebUI kopyaları (elle yazıldı). Enum'lar JSON'da metin gelir/gider.

public enum DoctorPanelAppointmentStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Completed,
}

public enum DoctorPanelGender
{
    Male,
    Female,
    Other,
}

public sealed class DoctorPanelAppointmentApiModel
{
    public Guid Id { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string BranchId { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public DoctorPanelAppointmentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    // Hastanın randevu alırken yazdığı şikâyet / not (yoksa null).
    public string? Note { get; set; }
    // Yalnız GET {id} yanıtında dolu olabilir; muayene detayı girilmediyse null.
    public DoctorPanelAppointmentDetailApiModel? Detail { get; set; }
}

public sealed class DoctorPanelAppointmentDetailApiModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string Complaint { get; set; } = string.Empty;
    public string? Diagnosis { get; set; }
    public string? DoctorNotes { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class DoctorPanelPrescriptionSummaryApiModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public int ItemCount { get; set; }
}

public sealed class DoctorPanelPrescriptionApiModel
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public string DoctorId { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public string? Notes { get; set; }
    public List<DoctorPanelPrescriptionItemApiModel> Items { get; set; } = [];
}

public sealed class DoctorPanelPrescriptionItemApiModel
{
    public int LineNo { get; set; }
    public string MedicineName { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public string? Instructions { get; set; }
}

public sealed class DoctorPanelReviewApiModel
{
    public string Id { get; set; } = string.Empty;
    public string DoctorId { get; set; } = string.Empty;
    public string ReviewerName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class DoctorPanelRatingSummaryApiModel
{
    public string DoctorId { get; set; } = string.Empty;
    public int ReviewCount { get; set; }
    public double AverageRating { get; set; }
    public int RecommendationPercent { get; set; }
}

public sealed class DoctorPanelUserApiModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

// GET doctors/{id} yanıtı. PUT gövdesi ayrı: bkz. DoctorPanelDoctorWriteBody.
public sealed class DoctorPanelDoctorApiModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DoctorPanelGender Gender { get; set; }
    public string? Degrees { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? ImageUrl { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public List<string> Services { get; set; } = [];
    public List<string> Gallery { get; set; } = [];
    public DoctorPanelSocialLinks SocialLinks { get; set; } = new();
    public List<DoctorPanelEducation> Educations { get; set; } = [];
    public List<DoctorPanelExperience> Experiences { get; set; } = [];
    public List<DoctorPanelAward> Awards { get; set; } = [];
    public List<DoctorPanelClinicLocation> ClinicLocations { get; set; } = [];
}

public sealed class DoctorPanelDoctorWriteBody
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DoctorPanelGender? Gender { get; set; }
    public string? Degrees { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? ImageUrl { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public List<string> Services { get; set; } = [];
    public List<string> Gallery { get; set; } = [];
    public DoctorPanelSocialLinks SocialLinks { get; set; } = new();
    public List<DoctorPanelEducation> Educations { get; set; } = [];
    public List<DoctorPanelExperience> Experiences { get; set; } = [];
    public List<DoctorPanelAward> Awards { get; set; } = [];
    public List<DoctorPanelClinicLocation> ClinicLocations { get; set; } = [];
}

public sealed class DoctorPanelSocialLinks
{
    public string? Facebook { get; set; }
    public string? X { get; set; }
    public string? Instagram { get; set; }
    public string? LinkedIn { get; set; }
    public string? Pinterest { get; set; }
    public string? YouTube { get; set; }
}

public sealed class DoctorPanelEducation
{
    public string School { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int EndYear { get; set; }
}

public sealed class DoctorPanelExperience
{
    public string Place { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int? EndYear { get; set; }
}

public sealed class DoctorPanelAward
{
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? Description { get; set; }
}

public sealed class DoctorPanelClinicLocation
{
    public string ClinicName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Address { get; set; }
    public string? Floor { get; set; }
    public decimal? HourlyFee { get; set; }
    public string Currency { get; set; } = "TRY";
    public List<DoctorPanelScheduleSlot> Schedule { get; set; } = [];
}

public sealed class DoctorPanelScheduleSlot
{
    public DayOfWeek DayOfWeek { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int SlotMinutes { get; set; } = 30;
}
