namespace Hastane.Identity.Api.Dtos;

// JWT-TASARIM.md §6: { accessToken, tokenType, expiresAt, user }
public class LoginResultDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    // UTC; istemci "ne zaman yeniden giriş yapmalıyım" diye buna bakar.
    public DateTime ExpiresAt { get; set; }
    public UserSummaryDto User { get; set; } = new();
}
