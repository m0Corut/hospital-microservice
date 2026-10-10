using System.ComponentModel.DataAnnotations;
using Hastane.WebUI.Models.Api;
using Hastane.WebUI.Models.Api.Public;

namespace Hastane.WebUI.ViewModels.Public;

public sealed class BranchesIndexViewModel
{
    public List<BranchApiModel> Branches { get; init; } = [];
}

// Arama formu (GET): alan adları adres çubuğundaki parametrelerdir (q, branchId, gender, city, minFee, maxFee, date, sort, page).
public sealed class DoctorSearchFilter
{
    [Display(Name = "Doktor ya da uzmanlık"), StringLength(100, ErrorMessage = "En çok 100 karakter.")]
    public string? Q { get; set; }

    [Display(Name = "Branş")]
    public string? BranchId { get; set; }

    [Display(Name = "Cinsiyet")]
    public string? Gender { get; set; }

    [Display(Name = "Şehir"), StringLength(100)]
    public string? City { get; set; }

    [Display(Name = "En düşük ücret"), Range(0, 1000000, ErrorMessage = "Ücret 0 ile 1.000.000 arasında olmalı.")]
    public decimal? MinFee { get; set; }

    [Display(Name = "En yüksek ücret"), Range(0, 1000000, ErrorMessage = "Ücret 0 ile 1.000.000 arasında olmalı.")]
    public decimal? MaxFee { get; set; }

    // Çalışma günü süzgeci: yyyy-MM-dd (API o tarihin haftanın gününe göre programı olan doktorları getirir).
    [Display(Name = "Müsait tarih")]
    public string? Date { get; set; }

    [Display(Name = "Sıralama")]
    public string? Sort { get; set; }

    [Range(1, 10000)]
    public int Page { get; set; } = 1;
}

public sealed class DoctorCardViewModel
{
    public required DoctorSearchItemApiModel Doctor { get; init; }
    public DoctorRatingSummaryApiModel? Rating { get; init; }
    public string? BranchName { get; init; }
    // Yalnız Patient rolünde doludur (sayfa başına tek favori çağrısından); kalp düğmesinin dolu/boş halini belirler.
    public bool IsFavorite { get; init; }
}

public sealed class DoctorSearchViewModel
{
    public const int PageSize = 12;

    public DoctorSearchFilter Filter { get; init; } = new();
    public List<BranchApiModel> Branches { get; init; } = [];
    public List<DoctorCardViewModel> Doctors { get; init; } = [];
    public long TotalCount { get; init; }
    public bool SearchFailed { get; init; }

    // Çoklu branş seçimi: form aynı ada birden çok değer gönderir, bağlayıcı virgülle birleştirir (API'nin branchIds biçimi).
    public HashSet<string> SelectedBranchIds => (Filter.BranchId ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

    public int TotalPages => (int)Math.Max(1, (TotalCount + PageSize - 1) / PageSize);
}

public sealed class ReviewFormModel
{
    [Display(Name = "Puan"), Range(1, 5, ErrorMessage = "Puan 1 ile 5 arasında olmalı.")]
    public int Rating { get; set; }

    [Display(Name = "Yorum"), StringLength(1000, ErrorMessage = "Yorum en çok 1000 karakter olabilir.")]
    public string? Comment { get; set; }
}

public sealed class ReviewRowViewModel
{
    public required ReviewApiModel Review { get; init; }

    // API yanıtındaki isMine (token'dan hesaplanır): düzenle/sil düğmeleri buna bağlı, ad eşleşmesine DEĞİL.
    // Asıl sahiplik denetimi yine API'dedir (başkasının yorumu 404 döner).
    public bool IsMine { get; init; }
}

public sealed class DoctorProfileViewModel
{
    public required DoctorDetailApiModel Doctor { get; init; }
    public string? BranchName { get; init; }
    public DoctorRatingSummaryApiModel? Summary { get; init; }
    public List<ReviewRowViewModel> Reviews { get; init; } = [];
    public bool ReviewsUnavailable { get; init; }
    public bool IsPatient { get; init; }
    public bool IsSignedIn { get; init; }
    public bool IsFavorite { get; init; }
    public ReviewFormModel ReviewForm { get; init; } = new() { Rating = 5 };

    public string FullName => DisplayText.DoctorName(Doctor.Title, Doctor.FirstName, Doctor.LastName);
}
