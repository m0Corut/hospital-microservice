using Hastane.WebUI.Models.Api.Admin;

namespace Hastane.WebUI.ViewModels.Admin;

public sealed class DoctorSearchIndexViewModel
{
    // Doctor servisine ulaşılamadıysa null (sayfa yine açılır, uyarı gösterir).
    public DoctorSearchIndexStatusApiModel? Status { get; init; }
}
