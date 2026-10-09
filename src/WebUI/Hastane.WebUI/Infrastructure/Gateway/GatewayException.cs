namespace Hastane.WebUI.Infrastructure.Gateway;

// İstemcinin varsayılan olarak fırlattığı "ekranın çözemeyeceği" hata: 401, 403, 404 ya da servis kapalı.
// GatewayExceptionFilter yakalar; ekran kodu try/catch yazmaz.
public sealed class GatewayException(ApiResultKind kind, int statusCode, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public ApiResultKind Kind { get; } = kind;
    public int StatusCode { get; } = statusCode;
}
