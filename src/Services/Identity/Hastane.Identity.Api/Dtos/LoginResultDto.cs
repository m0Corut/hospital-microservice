namespace Hastane.Identity.Api.Dtos;

// UserSummaryDto ile aynı alanlar + Message. (Dalga 3'te buraya JWT eklenecek.)
public class LoginResultDto
{
    public string Id { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
    public string Message { get; set; } = string.Empty;
}
