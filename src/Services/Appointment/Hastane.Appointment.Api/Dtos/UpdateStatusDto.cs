using System.ComponentModel.DataAnnotations;
using Hastane.Appointment.Api.Entities;

namespace Hastane.Appointment.Api.Dtos;

public class UpdateStatusDto
{
    // Nullable + Required: gövdede hiç gönderilmezse sessizce Pending (enum'un 0'ı) sayılmasın, 400 dönsün.
    [Required, EnumDataType(typeof(AppointmentStatus))]
    public AppointmentStatus? Status { get; set; }
}
