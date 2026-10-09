using System.ComponentModel.DataAnnotations;

namespace Hastane.Branch.Api.Dtos;

public class UpdateBranchDto
{
    [Required]
    public string Id { get; set; } = string.Empty;
    [Required]
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Icon { get; set; }
}
