using Hastane.WebUI.Infrastructure.Gateway;
using Hastane.WebUI.Models.Api;

namespace Hastane.WebUI.Clients;

// Profil fotoğrafı: yükleme (multipart) ve silme. Hedef kullanıcı token'dan gelir ("me"); gövdede kimlik yok.
public interface IAccountPhotoClient
{
    // PUT auth/me/photo -> 200 (kullanıcı özeti) | 400 (desteklenmeyen/boş dosya) | 413 (2 MB üstü).
    Task<ApiResult<UserSummaryApiModel>> UploadAsync(Stream content, string contentType, CancellationToken ct);

    // DELETE auth/me/photo -> 204 | 404 (fotoğraf yoktu).
    Task<ApiResult> DeleteAsync(CancellationToken ct);
}

public sealed class AccountPhotoClient(IGatewayClient gateway) : IAccountPhotoClient
{
    public Task<ApiResult<UserSummaryApiModel>> UploadAsync(Stream content, string contentType, CancellationToken ct)
        => gateway.PutFileAsync<UserSummaryApiModel>("/gateway/auth/me/photo", "file", "photo", contentType, content, ct: ct);

    public Task<ApiResult> DeleteAsync(CancellationToken ct)
        => gateway.DeleteAsync("/gateway/auth/me/photo", ApiFailures.NotFound, ct);
}
