namespace Hastane.Payment.Api.Integration;

// Ücret kuralı TEK yerde: Doctor servisi (DoctorDerived.MinFee/Currency). Payment hesaplamaz, yanıttaki alanları okur;
// WebUI de aynı alanları kullandığından ikisi tanım gereği aynı tutarı bulur.
public class DoctorFeeLookup(HttpClient http, IHttpContextAccessor accessor, ILogger<DoctorFeeLookup> logger) : IDoctorFeeLookup
{
    public async Task<LookupResult<DoctorFee>> GetFeeAsync(string doctorId, CancellationToken ct)
    {
        var result = await DownstreamClient.GetAsync<DoctorResponse>(
            http, $"/api/doctors/{Uri.EscapeDataString(doctorId)}", accessor, logger, ct);
        if (result.Outcome != LookupOutcome.Found) return new LookupResult<DoctorFee>(result.Outcome);

        var doctor = result.Value!;
        return LookupResult<DoctorFee>.Found(doctor.MinFee is > 0 && !string.IsNullOrWhiteSpace(doctor.Currency)
            ? new DoctorFee(doctor.MinFee, doctor.Currency)
            : new DoctorFee(null, null));
    }

    // Yalnız Doctor'ın türetilmiş alanları (GetByIdDoctorDto.MinFee / Currency).
    private sealed record DoctorResponse(decimal? MinFee, string? Currency);
}
