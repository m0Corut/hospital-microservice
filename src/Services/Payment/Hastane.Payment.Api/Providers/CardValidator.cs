namespace Hastane.Payment.Api.Providers;

// Kart biçim kontrolleri: Luhn, son kullanma tarihi, CVV, marka. Hata mesajları kart verisini İÇERMEZ.
public static class CardValidator
{
    public static bool PassesLuhn(string number)
    {
        if (number.Length < 12 || number.Length > 19) return false;
        var sum = 0;
        var doubleIt = false;
        for (var i = number.Length - 1; i >= 0; i--)
        {
            var c = number[i];
            if (c < '0' || c > '9') return false;
            var digit = c - '0';
            if (doubleIt)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }

    // Kart, son kullanma ayının SON gününün sonuna kadar geçerlidir.
    public static bool IsExpired(int expMonth, int expYear, DateTime utcNow)
    {
        var endOfMonth = new DateTime(expYear, expMonth, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        return utcNow >= endOfMonth;
    }

    public static bool IsValidCvv(string cvv) => cvv.Length is 3 or 4 && cvv.All(char.IsAsciiDigit);

    public static string BrandOf(string number) => number.Length == 0 ? "Unknown" : number[0] switch
    {
        '4' => "Visa",
        '5' => "Mastercard",
        '3' => "Amex",
        _ => "Unknown",
    };

    public static string Last4(string number) => number[^4..];

    // Geçersizse (kart verisi içermeyen) hata mesajı, geçerliyse null.
    public static string? Validate(string number, int expMonth, int expYear, string cvv, DateTime utcNow)
    {
        if (!PassesLuhn(number)) return "Geçersiz kart numarası.";
        if (expMonth is < 1 or > 12 || expYear is < 2000 or > 2200) return "Geçersiz son kullanma tarihi.";
        if (IsExpired(expMonth, expYear, utcNow)) return "Kartın son kullanma tarihi geçmiş.";
        if (!IsValidCvv(cvv)) return "CVV 3 veya 4 haneli olmalı.";
        return null;
    }
}
