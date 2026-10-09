using System.ComponentModel.DataAnnotations;

namespace Hastane.Identity.Api.Dtos;

public class LinkDoctorDto
{
    [Required, StringLength(64)]
    public string DoctorId { get; set; } = string.Empty;
}
