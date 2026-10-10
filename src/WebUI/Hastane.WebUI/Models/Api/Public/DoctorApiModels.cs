namespace Hastane.WebUI.Models.Api.Public;

// Doctor servisinin DTO'larının WebUI kopyası (yalnız ekranın kullandığı alanlar). Cinsiyet metin gelir: Male/Female/Other.
public sealed class PagedApiModel<T>
{
    public List<T> Items { get; set; } = [];
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class DoctorSearchItemApiModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Degrees { get; set; }
    public string? Gender { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public List<string> Services { get; set; } = [];
    // İlk 4 klinik fotoğrafı (kart önizlemesi).
    public List<string> Gallery { get; set; } = [];
    public string? PrimaryCity { get; set; }
    public decimal? MinFee { get; set; }
    public decimal? MaxFee { get; set; }
    public string? Currency { get; set; }
}

public sealed class DoctorSummaryApiModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Degrees { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

public sealed class DoctorDetailApiModel
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Gender { get; set; }
    public string? Degrees { get; set; }
    public string BranchId { get; set; } = string.Empty;
    public string? Biography { get; set; }
    public string? ImageUrl { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public List<string> Services { get; set; } = [];
    public List<string> Gallery { get; set; } = [];
    public SocialLinksApiModel SocialLinks { get; set; } = new();
    public List<EducationApiModel> Educations { get; set; } = [];
    public List<ExperienceApiModel> Experiences { get; set; } = [];
    public List<AwardApiModel> Awards { get; set; } = [];
    public List<ClinicLocationApiModel> ClinicLocations { get; set; } = [];
    public string? PrimaryCity { get; set; }
    public decimal? MinFee { get; set; }
    public decimal? MaxFee { get; set; }
    public string? Currency { get; set; }
}

// Herkese açık profilde gösterilen sosyal bağlantılar. Gösterimde yalnız http/https kabul edilir (UiKit.SafeSocialUrl).
public sealed class SocialLinksApiModel
{
    public string? Facebook { get; set; }
    public string? X { get; set; }
    public string? Instagram { get; set; }
    public string? LinkedIn { get; set; }
    public string? Pinterest { get; set; }
    public string? YouTube { get; set; }
}

public sealed class EducationApiModel
{
    public string School { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int EndYear { get; set; }
}

public sealed class ExperienceApiModel
{
    public string Place { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int StartYear { get; set; }
    public int? EndYear { get; set; }
}

public sealed class AwardApiModel
{
    public string Title { get; set; } = string.Empty;
    public int Year { get; set; }
    public string? Description { get; set; }
}

public sealed class ClinicLocationApiModel
{
    public string ClinicName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Address { get; set; }
    public string? Floor { get; set; }
    public decimal? HourlyFee { get; set; }
    public string Currency { get; set; } = "TRY";
    public List<ScheduleSlotApiModel> Schedule { get; set; } = [];
}

public sealed class ScheduleSlotApiModel
{
    // DayOfWeek metin gelir: Monday...
    public string DayOfWeek { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int SlotMinutes { get; set; }
}
