namespace Hastane.Patient.Api.Entities;

// Kod adları ASCII ("APos"); ekranda gösterilen "A Rh+" biçimi BloodTypes.Display ile üretilir.
public enum BloodType
{
    APos,
    ANeg,
    BPos,
    BNeg,
    ABPos,
    ABNeg,
    ZeroPos,
    ZeroNeg,
}

public static class BloodTypes
{
    public static string? Display(BloodType? type) => type switch
    {
        BloodType.APos => "A Rh+",
        BloodType.ANeg => "A Rh-",
        BloodType.BPos => "B Rh+",
        BloodType.BNeg => "B Rh-",
        BloodType.ABPos => "AB Rh+",
        BloodType.ABNeg => "AB Rh-",
        BloodType.ZeroPos => "0 Rh+",
        BloodType.ZeroNeg => "0 Rh-",
        _ => null,
    };
}
