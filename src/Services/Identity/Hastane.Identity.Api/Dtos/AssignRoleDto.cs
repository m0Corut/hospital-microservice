using System.ComponentModel.DataAnnotations;

namespace Hastane.Identity.Api.Dtos;

public class AssignRoleDto
{
    [Required, StringLength(50)]
    public string Role { get; set; } = string.Empty;
}
