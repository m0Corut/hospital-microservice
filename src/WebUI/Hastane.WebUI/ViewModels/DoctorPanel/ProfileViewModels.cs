using System.ComponentModel.DataAnnotations;
using Hastane.WebUI.Models.Api.DoctorPanel;

namespace Hastane.WebUI.ViewModels.DoctorPanel;

// Alan adları servisin UpdateDoctorDto'suyla AYNI: API'nin 400 yanıtındaki "Educations[0].School" gibi anahtarlar
// doğrudan form alanına oturur (ModelState.AddApiProblem).
public sealed class ProfileFormViewModel
{
    [Required(ErrorMessage = "Ad gerekli.")]
    [Display(Name = "Ad")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Soyad gerekli.")]
    [Display(Name = "Soyad")]
    public string LastName { get; set; } = string.Empty;

    [Display(Name = "Unvan")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Cinsiyet seçin.")]
    [Display(Name = "Cinsiyet")]
    public DoctorPanelGender? Gender { get; set; }

    [Display(Name = "Dereceler")]
    public string? Degrees { get; set; }

    [Required(ErrorMessage = "Branş seçin.")]
    [Display(Name = "Branş")]
    public string BranchId { get; set; } = string.Empty;

    [Display(Name = "Özgeçmiş")]
    public string? Biography { get; set; }

    [Display(Name = "Fotoğraf adresi")]
    public string? ImageUrl { get; set; }

    [Display(Name = "Telefon")]
    public string? Phone { get; set; }

    [Display(Name = "E-posta")]
    public string? Email { get; set; }

    public List<string?> Services { get; set; } = [];
    public List<string?> Gallery { get; set; } = [];
    public SocialLinksForm SocialLinks { get; set; } = new();
    public List<EducationForm> Educations { get; set; } = [];
    public List<ExperienceForm> Experiences { get; set; } = [];
    public List<AwardForm> Awards { get; set; } = [];
    public List<ClinicLocationForm> ClinicLocations { get; set; } = [];

    // Yalnız şube listesi gösterimi için (forma yazılmaz).
    public List<KeyValuePair<string, string>> BranchOptions { get; set; } = [];

    public static ProfileFormViewModel From(DoctorPanelDoctorApiModel d) => new()
    {
        FirstName = d.FirstName,
        LastName = d.LastName,
        Title = d.Title,
        Gender = d.Gender,
        Degrees = d.Degrees,
        BranchId = d.BranchId,
        Biography = d.Biography,
        ImageUrl = d.ImageUrl,
        Phone = d.Phone,
        Email = d.Email,
        Services = [.. d.Services],
        Gallery = [.. d.Gallery],
        SocialLinks = new SocialLinksForm
        {
            Facebook = d.SocialLinks.Facebook,
            X = d.SocialLinks.X,
            Instagram = d.SocialLinks.Instagram,
            LinkedIn = d.SocialLinks.LinkedIn,
            Pinterest = d.SocialLinks.Pinterest,
            YouTube = d.SocialLinks.YouTube,
        },
        Educations = d.Educations.Select(e => new EducationForm { School = e.School, Department = e.Department, StartYear = e.StartYear, EndYear = e.EndYear }).ToList(),
        Experiences = d.Experiences.Select(e => new ExperienceForm { Place = e.Place, Position = e.Position, StartYear = e.StartYear, EndYear = e.EndYear }).ToList(),
        Awards = d.Awards.Select(a => new AwardForm { Title = a.Title, Year = a.Year, Description = a.Description }).ToList(),
        ClinicLocations = d.ClinicLocations.Select(c => new ClinicLocationForm
        {
            ClinicName = c.ClinicName,
            City = c.City,
            District = c.District,
            Address = c.Address,
            Floor = c.Floor,
            HourlyFee = c.HourlyFee,
            Currency = c.Currency,
            Schedule = c.Schedule.Select(s => new ScheduleSlotForm { DayOfWeek = s.DayOfWeek, StartTime = s.StartTime, EndTime = s.EndTime, SlotMinutes = s.SlotMinutes }).ToList(),
        }).ToList(),
    };

    // Boş bırakılan liste satırları atılır; kimlik (Id) gövdeye CONTROLLER'da token'daki doctor_id ile konur.
    public DoctorPanelDoctorWriteBody ToWriteBody(string doctorId) => new()
    {
        Id = doctorId,
        FirstName = FirstName,
        LastName = LastName,
        Title = Title,
        Gender = Gender,
        Degrees = Degrees,
        BranchId = BranchId,
        Biography = Biography,
        ImageUrl = ImageUrl,
        Phone = Phone,
        Email = Email,
        Services = Services.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList(),
        Gallery = Gallery.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList(),
        SocialLinks = new DoctorPanelSocialLinks
        {
            Facebook = SocialLinks.Facebook,
            X = SocialLinks.X,
            Instagram = SocialLinks.Instagram,
            LinkedIn = SocialLinks.LinkedIn,
            Pinterest = SocialLinks.Pinterest,
            YouTube = SocialLinks.YouTube,
        },
        Educations = Educations.Select(e => new DoctorPanelEducation { School = e.School, Department = e.Department, StartYear = e.StartYear, EndYear = e.EndYear }).ToList(),
        Experiences = Experiences.Select(e => new DoctorPanelExperience { Place = e.Place, Position = e.Position, StartYear = e.StartYear, EndYear = e.EndYear }).ToList(),
        Awards = Awards.Select(a => new DoctorPanelAward { Title = a.Title, Year = a.Year, Description = a.Description }).ToList(),
        ClinicLocations = ClinicLocations.Select(c => new DoctorPanelClinicLocation
        {
            ClinicName = c.ClinicName,
            City = c.City,
            District = c.District,
            Address = c.Address,
            Floor = c.Floor,
            HourlyFee = c.HourlyFee,
            Currency = c.Currency,
            Schedule = c.Schedule.Select(s => new DoctorPanelScheduleSlot { DayOfWeek = s.DayOfWeek, StartTime = s.StartTime, EndTime = s.EndTime, SlotMinutes = s.SlotMinutes }).ToList(),
        }).ToList(),
    };
}

public sealed class SocialLinksForm
{
    public string? Facebook { get; set; }
    public string? X { get; set; }
    public string? Instagram { get; set; }
    public string? LinkedIn { get; set; }
    public string? Pinterest { get; set; }
    public string? YouTube { get; set; }
}

public sealed class EducationForm
{
    [Required(ErrorMessage = "Okul gerekli.")]
    public string School { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bölüm gerekli.")]
    public string Department { get; set; } = string.Empty;

    public int StartYear { get; set; }
    public int EndYear { get; set; }
}

public sealed class ExperienceForm
{
    [Required(ErrorMessage = "Çalışılan yer gerekli.")]
    public string Place { get; set; } = string.Empty;

    [Required(ErrorMessage = "Görev gerekli.")]
    public string Position { get; set; } = string.Empty;

    public int StartYear { get; set; }
    public int? EndYear { get; set; }
}

public sealed class AwardForm
{
    [Required(ErrorMessage = "Ödül adı gerekli.")]
    public string Title { get; set; } = string.Empty;

    public int Year { get; set; }
    public string? Description { get; set; }
}

public sealed class ClinicLocationForm
{
    [Required(ErrorMessage = "Klinik adı gerekli.")]
    public string ClinicName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şehir gerekli.")]
    public string City { get; set; } = string.Empty;

    public string? District { get; set; }
    public string? Address { get; set; }
    public string? Floor { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Ücret negatif olamaz.")]
    public decimal? HourlyFee { get; set; }

    [Required(ErrorMessage = "Para birimi gerekli.")]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Para birimi 3 büyük harf olmalı (ör. TRY).")]
    public string Currency { get; set; } = "TRY";

    public List<ScheduleSlotForm> Schedule { get; set; } = [];
}

public sealed class ScheduleSlotForm
{
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;

    [Required(ErrorMessage = "Başlangıç saati gerekli.")]
    public string StartTime { get; set; } = "09:00";

    [Required(ErrorMessage = "Bitiş saati gerekli.")]
    public string EndTime { get; set; } = "12:00";

    [Range(5, 120, ErrorMessage = "Randevu süresi 5-120 dakika olmalı.")]
    public int SlotMinutes { get; set; } = 30;
}
