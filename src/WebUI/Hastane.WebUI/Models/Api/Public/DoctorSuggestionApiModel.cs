namespace Hastane.WebUI.Models.Api.Public;

// Doctor servisi GET /api/doctors/suggest öğesi. Type: "doctor" | "service".
public sealed class DoctorSuggestionApiModel
{
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = "doctor";
    public string? DoctorId { get; set; }
}
