using System.ComponentModel.DataAnnotations;

namespace Hastane.Doctor.Api.Dtos;

public class UpdateDoctorDto : DoctorWriteDto
{
    [Required]
    public string Id { get; set; } = string.Empty;
}
