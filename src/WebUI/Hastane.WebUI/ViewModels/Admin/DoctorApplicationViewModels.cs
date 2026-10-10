using Hastane.WebUI.Models.Api;
using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.ViewModels.Admin;

public sealed class DoctorApplicationIndexViewModel
{
    public string? Status { get; init; }
    public required AdminPagedApiModel<DoctorApplicationApiModel> Page { get; init; }
    public Dictionary<string, string> BranchNames { get; init; } = [];

    // Az önce onaylanan başvurunun sahibi: "doktor kaydını oluşturun ve bağlayın" önerisi için.
    public string? ApprovedUserId { get; init; }
    public string? ApprovedName { get; init; }

    public string BranchName(string? id) => string.IsNullOrEmpty(id) ? "-" : BranchNames.GetValueOrDefault(id, id);

    public static string StatusText(string status) => DoctorApplicationStatusText(status);

    public static string StatusBadge(string status) => status switch
    {
        "Approved" => "text-bg-success",
        "Rejected" => "text-bg-danger",
        _ => "text-bg-warning",
    };

    private static string DoctorApplicationStatusText(string status) => status switch
    {
        "Pending" => "Bekliyor",
        "Approved" => "Onaylandı",
        "Rejected" => "Reddedildi",
        _ => status,
    };
}
