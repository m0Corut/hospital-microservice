using Hastane.WebUI.Models.Api.Queue;
using Hastane.WebUI.ViewModels.Shared;

namespace Hastane.WebUI.ViewModels.Queue;

public static class QueueText
{
    public static string Status(string status) => status switch
    {
        "Waiting" => "Bekliyor",
        "Called" => "Çağrıldı",
        "Completed" => "Tamamlandı",
        "NoShow" => "Gelmedi",
        _ => status,
    };

    public static string Badge(string status) => status switch
    {
        "Waiting" => "text-bg-warning",
        "Called" => "text-bg-primary",
        "Completed" => "text-bg-success",
        _ => "text-bg-secondary",
    };

    // Tarayıcı Gateway'e kendisi bağlanır: adres "Gateway:PublicBaseUrl" (yoksa sunucunun kullandığı "Gateway:BaseUrl").
    // http -> ws, https -> wss. Token taşımaz: hub anonim ve yalnız dinleme.
    public static string HubUrl(string gatewayBaseUrl, string? publicBaseUrl)
    {
        var baseUrl = (string.IsNullOrWhiteSpace(publicBaseUrl) ? gatewayBaseUrl : publicBaseUrl).TrimEnd('/');
        var ws = baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "wss://" + baseUrl[8..]
            : baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "ws://" + baseUrl[7..]
            : baseUrl;
        return ws + "/gateway/hubs/queue";
    }

    public static string Time(DateTime? utc) => utc is { } t ? AppointmentTime.ToClinicLocal(t).ToString("HH:mm") : string.Empty;
}

public sealed record DoctorOption(string Id, string Name);

// Herkese açık çağrı ekranı. Hub/yedek adresleri görünümde data-* olarak taşınır (token YOK: ekran anonim).
public sealed class QueueDisplayViewModel
{
    public string? DoctorId { get; init; }
    public string? DoctorName { get; init; }
    public IReadOnlyList<DoctorOption> Doctors { get; init; } = [];
    public QueueDisplayApiModel? Display { get; init; }

    // Tarayıcının SignalR ile bağlanacağı Gateway WebSocket adresi (ws:// ya da wss://).
    public string HubUrl { get; init; } = string.Empty;
    public string PollUrl { get; init; } = string.Empty;
    public bool Unavailable { get; init; }
}

public sealed record PatientQueueRow(QueueEntryApiModel Entry, string DoctorName);

public sealed class PatientQueueViewModel
{
    public IReadOnlyList<PatientQueueRow> Rows { get; init; } = [];
    public string HubUrl { get; init; } = string.Empty;
    public bool Unavailable { get; init; }
}

public sealed class DoctorQueueViewModel
{
    public string DoctorId { get; init; } = string.Empty;
    public QueueEntryApiModel? Current { get; init; }
    public IReadOnlyList<QueueEntryApiModel> Waiting { get; init; } = [];
    public IReadOnlyList<QueueEntryApiModel> Finished { get; init; } = [];
    public string HubUrl { get; init; } = string.Empty;
    public bool Unavailable { get; init; }
}
