using System.ComponentModel.DataAnnotations;
using Hastane.WebUI.Models.Api.Admin;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hastane.WebUI.ViewModels.Admin;

// Doktor formu. Özellik adları API gövdesiyle aynı (Educations[0].School...), böylece API'nin alan hataları
// ("Educations[0].School") ModelState'te doğrudan ilgili kutuya düşer.
public sealed class DoctorFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Ad gerekli."), StringLength(100)]
    [Display(Name = "Ad")]
    public string? FirstName { get; set; }

    [Required(ErrorMessage = "Soyad gerekli."), StringLength(100)]
    [Display(Name = "Soyad")]
    public string? LastName { get; set; }

    [Display(Name = "Unvan")]
    public string? Title { get; set; }

    [Required(ErrorMessage = "Cinsiyet seçin.")]
    [Display(Name = "Cinsiyet")]
    public AdminDoctorGender? Gender { get; set; }

    [StringLength(300, ErrorMessage = "Dereceler en çok 300 karakter olabilir.")]
    [Display(Name = "Dereceler")]
    public string? Degrees { get; set; }

    [Required(ErrorMessage = "Branş seçin.")]
    [Display(Name = "Branş")]
    public string? BranchId { get; set; }

    [Display(Name = "Biyografi")]
    public string? Biography { get; set; }

    [Display(Name = "Fotoğraf adresi")]
    public string? ImageUrl { get; set; }

    [Display(Name = "Telefon")]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "Geçerli bir e-posta girin.")]
    [Display(Name = "E-posta")]
    public string? Email { get; set; }

    public List<string?> Services { get; set; } = [];
    public List<string?> Gallery { get; set; } = [];
    public SocialLinksForm SocialLinks { get; set; } = new();
    public List<EducationForm> Educations { get; set; } = [];
    public List<ExperienceForm> Experiences { get; set; } = [];
    public List<AwardForm> Awards { get; set; } = [];
    public List<ClinicLocationForm> ClinicLocations { get; set; } = [];

    // Branş açılır listesi: formdan gelmez, her çizimde sunucu doldurur.
    [BindNever]
    public List<SelectListItem> BranchChoices { get; set; } = [];

    public static DoctorFormViewModel From(AdminDoctorApiModel d) => new()
    {
        Id = d.Id,
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
        Educations = d.Educations.Select(e => new EducationForm
        {
            School = e.School,
            Department = e.Department,
            StartYear = e.StartYear,
            EndYear = e.EndYear,
        }).ToList(),
        Experiences = d.Experiences.Select(e => new ExperienceForm
        {
            Place = e.Place,
            Position = e.Position,
            StartYear = e.StartYear,
            EndYear = e.EndYear,
        }).ToList(),
        Awards = d.Awards.Select(a => new AwardForm { Title = a.Title, Year = a.Year, Description = a.Description }).ToList(),
        ClinicLocations = d.ClinicLocations.Select(c => new ClinicLocationForm
        {
            ClinicName = c.ClinicName,
            City = c.City,
            District = c.District,
            Address = c.Address,
            Floor = c.Floor,
            HourlyFee = c.HourlyFee?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Currency = c.Currency,
            Schedule = c.Schedule.Select(s => new ScheduleSlotForm
            {
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                SlotMinutes = s.SlotMinutes,
            }).ToList(),
        }).ToList(),
    };

    // Boş kalan (yalnız boşluk içeren) liste öğeleri gövdeye girmez.
    public T ToBody<T>() where T : AdminDoctorWriteBody, new() => new()
    {
        FirstName = FirstName!.Trim(),
        LastName = LastName!.Trim(),
        Title = Clean(Title),
        Gender = Gender!.Value,
        Degrees = Clean(Degrees),
        BranchId = BranchId!,
        Biography = Clean(Biography),
        ImageUrl = Clean(ImageUrl),
        Phone = Clean(Phone),
        Email = Clean(Email),
        Services = Services.Select(Clean).OfType<string>().ToList(),
        Gallery = Gallery.Select(Clean).OfType<string>().ToList(),
        SocialLinks = new AdminSocialLinksApiModel
        {
            Facebook = Clean(SocialLinks.Facebook),
            X = Clean(SocialLinks.X),
            Instagram = Clean(SocialLinks.Instagram),
            LinkedIn = Clean(SocialLinks.LinkedIn),
            Pinterest = Clean(SocialLinks.Pinterest),
            YouTube = Clean(SocialLinks.YouTube),
        },
        Educations = Educations.Select(e => new AdminEducationApiModel
        {
            School = e.School!.Trim(),
            Department = e.Department!.Trim(),
            StartYear = e.StartYear ?? 0,
            EndYear = e.EndYear ?? 0,
        }).ToList(),
        Experiences = Experiences.Select(e => new AdminExperienceApiModel
        {
            Place = e.Place!.Trim(),
            Position = e.Position!.Trim(),
            StartYear = e.StartYear ?? 0,
            EndYear = e.EndYear,
        }).ToList(),
        Awards = Awards.Select(a => new AdminAwardApiModel { Title = a.Title!.Trim(), Year = a.Year ?? 0, Description = Clean(a.Description) }).ToList(),
        ClinicLocations = ClinicLocations.Select(c => new AdminClinicLocationApiModel
        {
            ClinicName = c.ClinicName!.Trim(),
            City = c.City!.Trim(),
            District = Clean(c.District),
            Address = Clean(c.Address),
            Floor = Clean(c.Floor),
            HourlyFee = ParseFee(c.HourlyFee),
            Currency = c.Currency!.Trim(),
            Schedule = c.Schedule.Select(s => new AdminScheduleSlotApiModel
            {
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime!.Trim(),
                EndTime = s.EndTime!.Trim(),
                SlotMinutes = s.SlotMinutes ?? 30,
            }).ToList(),
        }).ToList(),
    };

    // Ücret kutusu metin: sunucu kültürü virgül/nokta beklentisini değiştirmesin diye değişmez kültürle okunur ("150,5" da kabul).
    private static decimal? ParseFee(string? text)
        => decimal.TryParse(text?.Trim().Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var fee)
            ? fee
            : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Liste düğmesi komutunu uygular. Komut yoksa false (normal kaydetme). Uygulanınca ModelState'i çağıran temizler.
    public bool ApplyCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var parts = command.Split(':');
        switch (parts)
        {
            case ["add", var list]: Add(list); return true;
            case ["add", "Schedule", var clinic] when TryIndex(clinic, ClinicLocations.Count, out var c):
                ClinicLocations[c].Schedule.Add(new ScheduleSlotForm());
                return true;
            case ["remove", var list, var index]: Remove(list, index); return true;
            case ["remove", "Schedule", var clinic, var slot]:
                if (TryIndex(clinic, ClinicLocations.Count, out var ci) && TryIndex(slot, ClinicLocations[ci].Schedule.Count, out var si))
                    ClinicLocations[ci].Schedule.RemoveAt(si);
                return true;
            default: return false;
        }
    }

    private void Add(string list)
    {
        switch (list)
        {
            case "Services": Services.Add(string.Empty); break;
            case "Gallery": Gallery.Add(string.Empty); break;
            case "Educations": Educations.Add(new EducationForm()); break;
            case "Experiences": Experiences.Add(new ExperienceForm()); break;
            case "Awards": Awards.Add(new AwardForm()); break;
            case "ClinicLocations": ClinicLocations.Add(new ClinicLocationForm { Schedule = [new ScheduleSlotForm()] }); break;
        }
    }

    private void Remove(string list, string index)
    {
        switch (list)
        {
            case "Services" when TryIndex(index, Services.Count, out var i): Services.RemoveAt(i); break;
            case "Gallery" when TryIndex(index, Gallery.Count, out var i): Gallery.RemoveAt(i); break;
            case "Educations" when TryIndex(index, Educations.Count, out var i): Educations.RemoveAt(i); break;
            case "Experiences" when TryIndex(index, Experiences.Count, out var i): Experiences.RemoveAt(i); break;
            case "Awards" when TryIndex(index, Awards.Count, out var i): Awards.RemoveAt(i); break;
            case "ClinicLocations" when TryIndex(index, ClinicLocations.Count, out var i): ClinicLocations.RemoveAt(i); break;
        }
    }

    private static bool TryIndex(string text, int count, out int index)
        => int.TryParse(text, out index) && index >= 0 && index < count;
}

public sealed class SocialLinksForm
{
    [Display(Name = "Facebook")] public string? Facebook { get; set; }
    [Display(Name = "X")] public string? X { get; set; }
    [Display(Name = "Instagram")] public string? Instagram { get; set; }
    [Display(Name = "LinkedIn")] public string? LinkedIn { get; set; }
    [Display(Name = "Pinterest")] public string? Pinterest { get; set; }
    [Display(Name = "YouTube")] public string? YouTube { get; set; }
}

public sealed class EducationForm
{
    [Required(ErrorMessage = "Okul gerekli.")]
    [Display(Name = "Okul")]
    public string? School { get; set; }

    [Required(ErrorMessage = "Bölüm gerekli.")]
    [Display(Name = "Bölüm")]
    public string? Department { get; set; }

    [Range(1900, 2100, ErrorMessage = "Yıl 1900-2100 arasında olmalı.")]
    [Display(Name = "Başlangıç yılı")]
    public int? StartYear { get; set; }

    [Range(1900, 2100, ErrorMessage = "Yıl 1900-2100 arasında olmalı.")]
    [Display(Name = "Bitiş yılı")]
    public int? EndYear { get; set; }
}

public sealed class ExperienceForm
{
    [Required(ErrorMessage = "Kurum gerekli.")]
    [Display(Name = "Kurum")]
    public string? Place { get; set; }

    [Required(ErrorMessage = "Görev gerekli.")]
    [Display(Name = "Görev")]
    public string? Position { get; set; }

    [Range(1900, 2100, ErrorMessage = "Yıl 1900-2100 arasında olmalı.")]
    [Display(Name = "Başlangıç yılı")]
    public int? StartYear { get; set; }

    [Range(1900, 2100, ErrorMessage = "Yıl 1900-2100 arasında olmalı.")]
    [Display(Name = "Bitiş yılı")]
    public int? EndYear { get; set; }
}

public sealed class AwardForm
{
    [Required(ErrorMessage = "Ödül adı gerekli.")]
    [Display(Name = "Ödül")]
    public string? Title { get; set; }

    [Range(1900, 2100, ErrorMessage = "Yıl 1900-2100 arasında olmalı.")]
    [Display(Name = "Yıl")]
    public int? Year { get; set; }

    [Display(Name = "Açıklama")]
    public string? Description { get; set; }
}

public sealed class ClinicLocationForm
{
    [Required(ErrorMessage = "Klinik adı gerekli.")]
    [Display(Name = "Klinik adı")]
    public string? ClinicName { get; set; }

    [Required(ErrorMessage = "Şehir gerekli.")]
    [Display(Name = "Şehir")]
    public string? City { get; set; }

    [Display(Name = "İlçe")]
    public string? District { get; set; }

    [Display(Name = "Adres")]
    public string? Address { get; set; }

    [Display(Name = "Kat")]
    public string? Floor { get; set; }

    [RegularExpression(@"^\d{1,7}([.,]\d{1,2})?$", ErrorMessage = "Ücret sayı olmalı (ör. 450 ya da 450,50).")]
    [Display(Name = "Saatlik ücret")]
    public string? HourlyFee { get; set; }

    [Required(ErrorMessage = "Para birimi gerekli.")]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "Para birimi 3 büyük harf olmalı (ör. TRY).")]
    [Display(Name = "Para birimi")]
    public string? Currency { get; set; } = "TRY";

    public List<ScheduleSlotForm> Schedule { get; set; } = [];
}

public sealed class ScheduleSlotForm
{
    [Display(Name = "Gün")]
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;

    [Required(ErrorMessage = "Başlangıç saati gerekli.")]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Saat SS:dd biçiminde olmalı.")]
    [Display(Name = "Başlangıç")]
    public string? StartTime { get; set; }

    [Required(ErrorMessage = "Bitiş saati gerekli.")]
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Saat SS:dd biçiminde olmalı.")]
    [Display(Name = "Bitiş")]
    public string? EndTime { get; set; }

    [Range(5, 120, ErrorMessage = "Dilim 5-120 dakika olmalı.")]
    [Display(Name = "Dilim (dk)")]
    public int? SlotMinutes { get; set; } = 30;
}
