using Hastane.Doctor.Api.Entities;

namespace Hastane.Doctor.Api.Mapping;

// Türetilmiş alanlar: konumlardan HER İSTEKTE hesaplanır, veritabanında SAKLANMAZ.
// Saklansaydı ücret güncellendiğinde ikinci bir kopya da güncellenmeli, unutulunca kart ile detay çelişirdi.
public static class DoctorDerived
{
    // Para birimi karışık olabilir (bir konum TRY, biri USD); farklı birimleri kıyaslamak anlamsız.
    // Bu sınıf "randevu ücreti"nin TEK tanımıdır; Payment ve WebUI aynı MinFee/Currency alanlarını okur.
    // Kural: ücreti olan İLK konumun birimi esas alınır, min/max yalnız o birimdeki ücretlerden hesaplanır.
    private static (string Currency, List<decimal> Fees)? Fees(IEnumerable<ClinicLocation> locations)
    {
        // 0 (ya da negatif) ücret "tanımsız" sayılır: hesaba katılmaz (WebUI de fee <= 0'ı tanımsız sayar).
        var withFee = locations.Where(l => l.HourlyFee is > 0).ToList();
        if (withFee.Count == 0) return null;
        var currency = withFee[0].Currency;
        return (currency, withFee.Where(l => l.Currency == currency).Select(l => l.HourlyFee!.Value).ToList());
    }

    public static decimal? MinFee(IEnumerable<ClinicLocation> locations) => Fees(locations)?.Fees.Min();
    public static decimal? MaxFee(IEnumerable<ClinicLocation> locations) => Fees(locations)?.Fees.Max();
    public static string? Currency(IEnumerable<ClinicLocation> locations) => Fees(locations)?.Currency;
    public static string? PrimaryCity(IEnumerable<ClinicLocation> locations) => locations.FirstOrDefault()?.City;
}
