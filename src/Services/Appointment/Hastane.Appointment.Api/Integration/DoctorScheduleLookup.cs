namespace Hastane.Appointment.Api.Integration;

// Program verisinin sahibi Doctor'dur (MongoDB). Appointment kopyalamaz, her seferinde sorar: program değişince
// iki serviste ayrışan iki gerçek olmaz.
public class DoctorScheduleLookup(HttpClient http, IHttpContextAccessor accessor, ILogger<DoctorScheduleLookup> logger)
    : IDoctorScheduleLookup
{
    public async Task<LookupResult<DoctorSchedule>> GetScheduleAsync(string doctorId, CancellationToken ct)
    {
        var result = await DownstreamClient.GetAsync<DoctorResponse>(
            http, $"/api/doctors/{Uri.EscapeDataString(doctorId)}", accessor, logger, ct);
        if (result.Outcome != LookupOutcome.Found) return new LookupResult<DoctorSchedule>(result.Outcome);

        return LookupResult<DoctorSchedule>.Found(new DoctorSchedule(result.Value!.ClinicLocations ?? []));
    }

    private sealed record DoctorResponse(List<ScheduleLocation>? ClinicLocations);
}
