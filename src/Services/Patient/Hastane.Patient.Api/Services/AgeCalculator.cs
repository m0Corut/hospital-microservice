namespace Hastane.Patient.Api.Services;

public static class AgeCalculator
{
    // Tam yıl: bu yılki doğum günü henüz gelmediyse bir eksik. 29 Şubat doğumlular için artık olmayan yılda doğum günü 28 Şubat sayılır (AddYears günü sıkıştırır).
    public static int? Calculate(DateOnly? birth, DateOnly today)
    {
        if (birth is null) return null;
        var age = today.Year - birth.Value.Year;
        if (today < birth.Value.AddYears(age)) age--;
        return Math.Max(age, 0);
    }
}
