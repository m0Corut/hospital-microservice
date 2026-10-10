namespace Hastane.WebUI.Models.Api.Admin;

// Doctor servisi JSON'u. Enum'lar metin olarak gider/gelir (Gender: Male/Female/Other, DayOfWeek: Monday...).
public enum AdminDoctorGender
{
    Male,
    Female,
    Other,
}

public sealed class AdminPagedApiModel<T>
{
    public List<T> Items { get; set; } = [];
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// GET /gateway/doctors/search ve GET /gateway/doctors liste satırı (telefon/e-posta yok).
public sealed class AdminDoctorListItemApiModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Degrees { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? PrimaryCity { get; set; }
    public decimal? MinFee { get; set; }
    public decimal? MaxFee { get; set; }
    public string? Currency { get; set; }
}

public sealed class AdminEducationApiModel
{
    public string School { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int EndYear { get; set; }
}

public sealed class AdminExperienceApiModel
{
    public string Place { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int? EndYear { get; set; }
}

public sealed class AdminAwardApiModel
{
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? Description { get; set; }
}

public sealed class AdminScheduleSlotApiModel
{
    public DayOfWeek DayOfWeek { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int SlotMinutes { get; set; } = 30;
}

public sealed class AdminClinicLocationApiModel
{
    public string ClinicName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Address { get; set; }
    public string? Floor { get; set; }
    public decimal? HourlyFee { get; set; }
    public string Currency { get; set; } = "TRY";
    public List<AdminScheduleSlotApiModel> Schedule { get; set; } = [];
}

public sealed class AdminSocialLinksApiModel
{
    public string? Facebook { get; set; }
    public string? X { get; set; }
    public string? Instagram { get; set; }
    public string? LinkedIn { get; set; }
    public string? Pinterest { get; set; }
    public string? YouTube { get; set; }
}

// GET /gateway/doctors/{id}
public sealed class AdminDoctorApiModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public AdminDoctorGender Gender { get; set; }
    public string? Degrees { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? ImageUrl { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public List<string> Services { get; set; } = [];
    public List<string> Gallery { get; set; } = [];
    public AdminSocialLinksApiModel SocialLinks { get; set; } = new();
    public List<AdminEducationApiModel> Educations { get; set; } = [];
    public List<AdminExperienceApiModel> Experiences { get; set; } = [];
    public List<AdminAwardApiModel> Awards { get; set; } = [];
    public List<AdminClinicLocationApiModel> ClinicLocations { get; set; } = [];
}

// POST /gateway/doctors gövdesi (Id yok).
public class AdminDoctorWriteBody
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public AdminDoctorGender Gender { get; set; }
    public string? Degrees { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? ImageUrl { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public List<string> Services { get; set; } = [];
    public List<string> Gallery { get; set; } = [];
    public AdminSocialLinksApiModel SocialLinks { get; set; } = new();
    public List<AdminEducationApiModel> Educations { get; set; } = [];
    public List<AdminExperienceApiModel> Experiences { get; set; } = [];
    public List<AdminAwardApiModel> Awards { get; set; } = [];
    public List<AdminClinicLocationApiModel> ClinicLocations { get; set; } = [];
}

// PUT /gateway/doctors/{id} gövdesi (yoldaki id ile aynı Id şart).
public sealed class AdminDoctorUpdateBody : AdminDoctorWriteBody
{
    public string Id { get; set; } = string.Empty;
}
