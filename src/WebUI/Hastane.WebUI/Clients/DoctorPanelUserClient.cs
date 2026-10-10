using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.DoctorPanel;

namespace Hastane.WebUI.Clients;

public interface IDoctorPanelUserClient
{
    // Kimlik -> görünen ad. Tek toplu çağrı (N+1 yok); erişim reddedilir ya da servis kapalıysa boş sözlük döner.
    Task<IReadOnlyDictionary<string, string>> GetNamesAsync(IEnumerable<string> ids, CancellationToken ct);
}

public sealed class DoctorPanelUserClient(IGatewayClient gateway) : IDoctorPanelUserClient
{
    private const int MaxIds = 100; // Identity tek istekte en çok 100 kullanıcı verir

    public async Task<IReadOnlyDictionary<string, string>> GetNamesAsync(IEnumerable<string> ids, CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().Chunk(MaxIds))
        {
            // Ad göstermek süs: hata olursa sayfa yine açılsın, Id kısaltmasına düşülür.
            var result = await gateway.GetAsync<List<DoctorPanelUserApiModel>>(
                $"/gateway/users?ids={Uri.EscapeDataString(string.Join(',', chunk))}", ApiFailures.All, ct);
            if (!result.TryGetValue(out var users)) continue;
            foreach (var u in users)
                names[u.Id] = $"{u.FirstName} {u.LastName}".Trim();
        }

        return names;
    }
}
