using Hastane.WebUI.Clients;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.ViewModels.Public;

namespace Hastane.WebUI.Areas.Patient.Controllers;

// Liste ekranlarında doktor adı için TEK toplu çağrı (N+1 yok). Doctor servisi kapalıysa liste yine açılır, ad "Doktor" görünür.
public sealed class DoctorNameLookup
{
    private readonly Dictionary<string, string> _names;

    private DoctorNameLookup(Dictionary<string, string> names) => _names = names;

    public static async Task<DoctorNameLookup> LoadAsync(IPublicDoctorClient doctors, IEnumerable<string> ids, CancellationToken ct)
    {
        var unique = ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList();
        var names = new Dictionary<string, string>();
        if (unique.Count == 0) return new DoctorNameLookup(names);

        var result = await doctors.GetBatchAsync(unique, ApiFailures.All, ct);
        if (result.Value is not null)
            foreach (var d in result.Value)
                names[d.Id] = DisplayText.DoctorName(d.Title, d.FirstName, d.LastName);

        return new DoctorNameLookup(names);
    }

    public string NameOf(string doctorId) => _names.GetValueOrDefault(doctorId, "Doktor");
}
