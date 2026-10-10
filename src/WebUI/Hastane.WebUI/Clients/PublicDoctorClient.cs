using System.Globalization;
using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api.Public;

namespace Hastane.WebUI.Clients;

// Arama ölçütleri; alan adları Doctor servisinin /search uç parametreleriyle bire bir (text, branchIds, gender, city, minFee, maxFee, date, sort, page, pageSize).
public sealed record DoctorSearchCriteria(
    string? Text, string? BranchId, string? Gender, string? City, decimal? MinFee, decimal? MaxFee,
    string? Date, string? Sort, int Page, int PageSize);

public interface IPublicDoctorClient
{
    Task<ApiResult<PagedApiModel<DoctorSearchItemApiModel>>> SearchAsync(DoctorSearchCriteria criteria, CancellationToken ct)
        => SearchAsync(criteria, ApiFailures.None, ct);

    // Ana sayfa "öne çıkan doktorlar" gibi yardımcı bölümler servis kapalıyken sayfayı düşürmesin diye hata kümesi verilebilir.
    Task<ApiResult<PagedApiModel<DoctorSearchItemApiModel>>> SearchAsync(DoctorSearchCriteria criteria, ApiFailures handle, CancellationToken ct);

    // Ayrıntı uç noktası giriş ister (telefon + e-posta içerir): anonimde istemci 401 fırlatır, filtre girişe yönlendirir.
    Task<ApiResult<DoctorDetailApiModel>> GetAsync(string id, CancellationToken ct);

    // Toplu özet (N+1 yok): randevu / reçete listelerinde doktor adı için. En çok 100 id.
    Task<ApiResult<List<DoctorSummaryApiModel>>> GetBatchAsync(IEnumerable<string> ids, ApiFailures handle, CancellationToken ct);
}

public sealed class PublicDoctorClient(IGatewayClient gateway) : IPublicDoctorClient
{
    public Task<ApiResult<PagedApiModel<DoctorSearchItemApiModel>>> SearchAsync(DoctorSearchCriteria c, ApiFailures handle, CancellationToken ct)
    {
        var query = new List<string>();
        Add(query, "text", c.Text);
        Add(query, "branchIds", c.BranchId);
        Add(query, "gender", c.Gender);
        Add(query, "city", c.City);
        Add(query, "minFee", c.MinFee?.ToString(CultureInfo.InvariantCulture));
        Add(query, "maxFee", c.MaxFee?.ToString(CultureInfo.InvariantCulture));
        Add(query, "date", c.Date);
        Add(query, "sort", c.Sort);
        query.Add("page=" + c.Page.ToString(CultureInfo.InvariantCulture));
        query.Add("pageSize=" + c.PageSize.ToString(CultureInfo.InvariantCulture));
        return gateway.GetAsync<PagedApiModel<DoctorSearchItemApiModel>>("/gateway/doctors/search?" + string.Join('&', query), handle, ct);
    }

    public Task<ApiResult<DoctorDetailApiModel>> GetAsync(string id, CancellationToken ct)
        => gateway.GetAsync<DoctorDetailApiModel>($"/gateway/doctors/{Uri.EscapeDataString(id)}", ct: ct);

    public Task<ApiResult<List<DoctorSummaryApiModel>>> GetBatchAsync(IEnumerable<string> ids, ApiFailures handle, CancellationToken ct)
    {
        var joined = string.Join(',', ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().Take(100));
        return gateway.GetAsync<List<DoctorSummaryApiModel>>("/gateway/doctors/batch?ids=" + Uri.EscapeDataString(joined), handle, ct);
    }

    private static void Add(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            query.Add(key + "=" + Uri.EscapeDataString(value.Trim()));
    }
}
