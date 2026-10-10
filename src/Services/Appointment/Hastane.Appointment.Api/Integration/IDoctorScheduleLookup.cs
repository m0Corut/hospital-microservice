namespace Hastane.Appointment.Api.Integration;

public interface IDoctorScheduleLookup
{
    Task<LookupResult<DoctorSchedule>> GetScheduleAsync(string doctorId, CancellationToken ct);
}

// Doctor'ın yanıtından yalnız programla ilgili alanlar (ProjectReference yok, kendi küçük modelimiz).
public record DoctorSchedule(List<ScheduleLocation> ClinicLocations);

public record ScheduleLocation(string ClinicName, string City, List<ScheduleEntry>? Schedule);

// StartTime/EndTime "HH:mm" yerel klinik saati.
public record ScheduleEntry(DayOfWeek DayOfWeek, string StartTime, string EndTime, int SlotMinutes);
