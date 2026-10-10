using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Hastane.Shared.Security;
using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Chat;
using Hastane.WebUI.Models.Api.Patient;
using Hastane.WebUI.ViewModels.Public;

namespace Hastane.WebUI.ViewModels.Social;

// ---------- Hasta profili ----------

public static class PatientProfileOptions
{
    public static readonly IReadOnlyList<(string Value, string Label)> Genders =
        [("Male", "Erkek"), ("Female", "Kadın"), ("Other", "Diğer")];

    // Değer: API'nin enum adı; etiket: ekran metni ("A Rh+").
    public static readonly IReadOnlyList<(string Value, string Label)> BloodTypes =
    [
        ("APos", "A Rh+"), ("ANeg", "A Rh-"), ("BPos", "B Rh+"), ("BNeg", "B Rh-"),
        ("ABPos", "AB Rh+"), ("ABNeg", "AB Rh-"), ("ZeroPos", "0 Rh+"), ("ZeroNeg", "0 Rh-"),
    ];

    public const string PhonePattern = @"^\+?[0-9 ()\-]{7,20}$";
    public const string PhoneMessage = "Telefon yalnız rakam, boşluk, +, -, ( ) içermeli ve 7-20 karakter olmalı.";
}

public sealed class PatientProfileFormViewModel
{
    [Display(Name = "Doğum tarihi")]
    public DateOnly? DateOfBirth { get; set; }

    [Display(Name = "Cinsiyet")]
    [RegularExpression("^(Male|Female|Other)?$", ErrorMessage = "Geçerli bir cinsiyet seçin.")]
    public string? Gender { get; set; }

    [Display(Name = "Kan grubu")]
    [RegularExpression("^(APos|ANeg|BPos|BNeg|ABPos|ABNeg|ZeroPos|ZeroNeg)?$", ErrorMessage = "Geçerli bir kan grubu seçin.")]
    public string? BloodType { get; set; }

    [Display(Name = "Telefon")]
    [StringLength(20, ErrorMessage = "Telefon en çok 20 karakter olabilir.")]
    [RegularExpression(PatientProfileOptions.PhonePattern, ErrorMessage = PatientProfileOptions.PhoneMessage)]
    public string? Phone { get; set; }

    [Display(Name = "Adres")]
    [StringLength(300, ErrorMessage = "Adres en çok 300 karakter olabilir.")]
    public string? Address { get; set; }

    [Display(Name = "Şehir")]
    [StringLength(60, ErrorMessage = "Şehir en çok 60 karakter olabilir.")]
    public string? City { get; set; }

    [Display(Name = "Acil durum kişisi")]
    [StringLength(100, ErrorMessage = "Ad en çok 100 karakter olabilir.")]
    public string? EmergencyContactName { get; set; }

    [Display(Name = "Acil durum telefonu")]
    [StringLength(20, ErrorMessage = "Telefon en çok 20 karakter olabilir.")]
    [RegularExpression(PatientProfileOptions.PhonePattern, ErrorMessage = PatientProfileOptions.PhoneMessage)]
    public string? EmergencyContactPhone { get; set; }

    // Salt okunur: sunucu doğum tarihinden hesaplar. Forma bağlanmaz (POST'ta yok sayılır).
    public int? Age { get; set; }

    public static PatientProfileFormViewModel From(PatientProfileApiModel p) => new()
    {
        DateOfBirth = p.DateOfBirth,
        Gender = p.Gender,
        BloodType = p.BloodType,
        Phone = p.Phone,
        Address = p.Address,
        City = p.City,
        EmergencyContactName = p.EmergencyContactName,
        EmergencyContactPhone = p.EmergencyContactPhone,
        Age = p.Age,
    };

    public UpdatePatientProfileRequest ToRequest() => new(
        DateOfBirth, Clean(Gender), Clean(BloodType), Clean(Phone), Clean(Address), Clean(City),
        Clean(EmergencyContactName), Clean(EmergencyContactPhone));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// ---------- Favoriler ----------

// Kalp düğmesi yalnız Patient rolünde çizilir; hangi doktorların favori olduğu sayfa başına TEK çağrıyla alınır.
public static class FavoriteLookup
{
    public static async Task<HashSet<string>> LoadAsync(ClaimsPrincipal user, IPatientProfileClient client, CancellationToken ct)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!user.IsInRole(HastaneRoles.Patient)) return set;

        // Servis kapalıysa kartlar kalpsiz-dolu değil, boş kalp olarak çizilir; sayfa bozulmaz.
        var result = await client.GetFavoritesAsync(ApiFailures.All, ct);
        if (result.Value is not null)
            foreach (var f in result.Value) set.Add(f.DoctorId);
        return set;
    }
}

public sealed record FavoriteButtonViewModel(string DoctorId, bool IsFavorite, bool Block = false);

public sealed class FavoritesViewModel
{
    public List<DoctorCardViewModel> Doctors { get; init; } = [];
}

// ---------- Bildirimler ----------

public sealed class NotificationRowViewModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    // Yalnız güvenli yerel yol; değilse null (bağlantı gösterilmez).
    public string? SafeLink { get; init; }
    public required DateTime CreatedAt { get; init; }
    public bool IsRead { get; init; }
}

public sealed class NotificationsViewModel
{
    public List<NotificationRowViewModel> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
    public long UnreadCount { get; init; }

    public int TotalPages => (int)Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
}

// ---------- Mesajlar ----------

public sealed record ConversationRowViewModel(string Id, string OtherName, string? Preview, DateTime LastMessageAt, int Unread);

public sealed record MessageRowViewModel(string Id, bool Mine, string SenderName, string Text, DateTime SentAt)
{
    public string TimeText => DisplayText.DateTimeLocal(SentAt);
}

public sealed class SendMessageForm
{
    [Display(Name = "Mesaj")]
    [Required(ErrorMessage = "Mesaj boş olamaz.")]
    [StringLength(2000, MinimumLength = 1, ErrorMessage = "Mesaj 1 ile 2000 karakter arasında olmalı.")]
    public string Text { get; set; } = string.Empty;
}

public sealed class MessagesViewModel
{
    public List<ConversationRowViewModel> Conversations { get; init; } = [];
    public ConversationRowViewModel? Selected { get; init; }
    public List<MessageRowViewModel> Messages { get; init; } = [];
    public SendMessageForm Form { get; init; } = new();

    // Yoklama başlangıcı: görünen son mesajın zamanı (ISO, Z).
    public string? LastSentAtIso => Messages.Count == 0 ? null : ChatTime.Iso(Messages[^1].SentAt);

    public static MessageRowViewModel ToRow(MessageApiModel m, bool isPatientSide)
        => new(m.Id, string.Equals(m.SenderRole, isPatientSide ? HastaneRoles.Patient : HastaneRoles.Doctor, StringComparison.OrdinalIgnoreCase),
            m.SenderName, m.Text, m.SentAt);
}
