namespace Hastane.Patient.Api.Integration;

public interface IAppointmentLookup
{
    // Çağıranın (Authorization başlığı) görebildiği randevular arasından bu hastaya ait olanlar.
    // Doctor için Appointment servisi zaten yalnız doktorun KENDİ randevularını döndürür.
    Task<LookupResult<List<AppointmentRef>>> GetForPatientAsync(string patientId, CancellationToken ct);
}
