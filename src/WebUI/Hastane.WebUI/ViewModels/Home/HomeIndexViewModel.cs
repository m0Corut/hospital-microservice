using Hastane.WebUI.Models.Api;

namespace Hastane.WebUI.ViewModels.Home;

public sealed class HomeIndexViewModel
{
    public List<BranchApiModel> Branches { get; init; } = [];
    public bool BranchesUnavailable { get; init; }
}
