using System.ComponentModel.DataAnnotations;

namespace Hastane.Identity.Api.Dtos;

public class LoginDto
{
    [Required, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
}
