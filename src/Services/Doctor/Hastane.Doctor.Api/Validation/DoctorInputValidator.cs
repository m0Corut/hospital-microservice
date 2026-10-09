using System.Text.RegularExpressions;
using Hastane.Doctor.Api.Dtos;

namespace Hastane.Doctor.Api.Validation;

// Servis katmanı savunması: DTO öznitelikleri bir gün atlanırsa (başka çağıran, birim test, öznitelik silinmesi)
// bozuk veri yine de MongoDB'ye girmez. Kurallar DoctorRules'tan gelir; DTO ile aynı sınırlar.
public static class DoctorInputValidator
{
    public static void ValidateOrThrow(DoctorWriteDto dto)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list)) errors[key] = list = [];
            list.Add(message);
        }

        if (string.IsNullOrWhiteSpace(dto.FirstName)) Add(nameof(dto.FirstName), "Zorunlu.");
        if (string.IsNullOrWhiteSpace(dto.LastName)) Add(nameof(dto.LastName), "Zorunlu.");
        if (string.IsNullOrWhiteSpace(dto.BranchId)) Add(nameof(dto.BranchId), "Zorunlu.");
        if (dto.Gender is null || !Enum.IsDefined(dto.Gender.Value)) Add(nameof(dto.Gender), "Zorunlu ve Male, Female, Other değerlerinden biri olmalı.");
        if (dto.Degrees is { Length: > DoctorRules.MaxDegrees }) Add(nameof(dto.Degrees), $"En çok {DoctorRules.MaxDegrees} karakter.");

        if (dto.Services is null || dto.Services.Count > DoctorRules.MaxServices)
            Add(nameof(dto.Services), $"En çok {DoctorRules.MaxServices} öğe.");
        else if (dto.Services.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > DoctorRules.MaxServiceLength))
            Add(nameof(dto.Services), $"Her öğe boş olmamalı ve en çok {DoctorRules.MaxServiceLength} karakter olmalı.");

        if (dto.Gallery is null || dto.Gallery.Count > DoctorRules.MaxGallery)
            Add(nameof(dto.Gallery), $"En çok {DoctorRules.MaxGallery} öğe.");
        else if (dto.Gallery.Any(u => !DoctorRules.IsHttpUrl(u, DoctorRules.MaxUrlLength)))
            Add(nameof(dto.Gallery), $"Her öğe en çok {DoctorRules.MaxUrlLength} karakterlik geçerli http/https adresi olmalı.");

        if (dto.SocialLinks is null) Add(nameof(dto.SocialLinks), "Boş olamaz.");
        else
        {
            foreach (var (name, value) in new (string, string?)[]
            {
                ("Facebook", dto.SocialLinks.Facebook), ("X", dto.SocialLinks.X), ("Instagram", dto.SocialLinks.Instagram),
                ("LinkedIn", dto.SocialLinks.LinkedIn), ("Pinterest", dto.SocialLinks.Pinterest), ("YouTube", dto.SocialLinks.YouTube),
            })
            {
                if (!string.IsNullOrEmpty(value) && !DoctorRules.IsHttpUrl(value, DoctorRules.MaxSocialLength))
                    Add($"SocialLinks.{name}", $"En çok {DoctorRules.MaxSocialLength} karakterlik geçerli http/https adresi olmalı.");
            }
        }

        var locations = dto.ClinicLocations ?? [];
        for (var i = 0; i < locations.Count; i++)
        {
            var loc = locations[i];
            var key = $"ClinicLocations[{i}]";
            if (loc is null) { Add(key, "Boş olamaz."); continue; }
            if (loc.HourlyFee is < 0) Add($"{key}.HourlyFee", "Negatif olamaz.");
            if (loc.Currency is null || !Regex.IsMatch(loc.Currency, DoctorRules.CurrencyPattern))
                Add($"{key}.Currency", "3 harfli büyük harf kod olmalı (ör. TRY).");
            foreach (var m in DoctorRules.ValidateSchedule(loc.Schedule)) Add($"{key}.Schedule", m);
        }

        if (errors.Count > 0)
            throw new DoctorValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}

// Controller bunu 400 ValidationProblem'e çevirir.
public class DoctorValidationException(IDictionary<string, string[]> errors) : Exception("Doğrulama hatası.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public DoctorValidationException(string key, string message) : this(new Dictionary<string, string[]> { [key] = [message] })
    {
    }
}
