namespace Hastane.Appointment.Api.Dtos;

// Boş bir randevu aralığı. Start/End UTC (JSON'da "Z" ile biter); istemci kendi saatine çevirir.
public class AvailableSlotDto
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string ClinicName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}
