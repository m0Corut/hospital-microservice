using Hastane.WebUI.Models.Api;
using Hastane.WebUI.ViewModels.Public;

namespace Hastane.WebUI.ViewModels.Home;

public sealed class HomeIndexViewModel
{
    public List<BranchApiModel> Branches { get; init; } = [];
    public bool BranchesUnavailable { get; init; }

    // "Öne çıkan doktorlar": arama ucunun ilk birkaç kaydı + puan özeti. Servis kapalıysa bölüm uyarıyla gösterilir.
    public List<DoctorCardViewModel> FeaturedDoctors { get; init; } = [];
    public bool FeaturedUnavailable { get; init; }
}
