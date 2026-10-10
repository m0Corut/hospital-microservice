using Hastane.WebUI.Models.Api.Analytics;

namespace Hastane.WebUI.ViewModels.Admin;

public sealed class AnalyticsViewModel
{
    public required int Days { get; init; }

    // Analytics servisine ulaşılamadıysa null (sayfa yine açılır, uyarı gösterir).
    public AnalyticsStatusApiModel? Status { get; init; }

    public IReadOnlyList<AnalyticsDoctorRow>? Doctors { get; init; }
    public IReadOnlyList<TopSearchTermApiModel>? Terms { get; init; }

    public bool ServiceDown => Status is null;
}

// Çubuk satırı: ad Doctor servisinden toplu çekilir (bulunamazsa genel ad); Percent en kalabalık doktora göre 0-100.
public sealed record AnalyticsDoctorRow(string DoctorId, string Name, long Views, int Percent);
