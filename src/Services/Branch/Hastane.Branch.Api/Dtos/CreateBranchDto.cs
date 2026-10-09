using System.ComponentModel.DataAnnotations;

namespace Hastane.Branch.Api.Dtos;

public class CreateBranchDto
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
}
