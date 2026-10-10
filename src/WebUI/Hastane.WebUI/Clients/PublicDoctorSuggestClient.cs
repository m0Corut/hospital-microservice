using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Public;

namespace Hastane.WebUI.Clients;

// Arama kutusu önerisi (anonim). Yardımcı bilgi: servis kapalıysa ya da hata verirse boş liste, arama kutusu çalışmaya devam eder.
public interface IPublicDoctorSuggestClient
{
    Task<List<DoctorSuggestionApiModel>> SuggestAsync(string q, CancellationToken ct);
}

public sealed class PublicDoctorSuggestClient(IGatewayClient gateway) : IPublicDoctorSuggestClient
{
    public async Task<List<DoctorSuggestionApiModel>> SuggestAsync(string q, CancellationToken ct)
    {
        var result = await gateway.GetAsync<List<DoctorSuggestionApiModel>>(
            "/gateway/doctors/suggest?q=" + Uri.EscapeDataString(q), ApiFailures.All, ct);
        return result.TryGetValue(out var list) ? list : [];
    }
}
