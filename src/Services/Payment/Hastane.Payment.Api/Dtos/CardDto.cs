using System.ComponentModel.DataAnnotations;

namespace Hastane.Payment.Api.Dtos;

// Kart verisi yalnız bu istekte, bellekte yaşar: sağlayıcıya gider, veritabanına/loga/cevaba YAZILMAZ.
// ToString bilerek maskeli: yanlışlıkla loglanırsa bile numara çıkmaz.
public sealed class CardDto
{
    [Required, RegularExpression(@"^\d{12,19}$", ErrorMessage = "Kart numarası 12-19 haneli rakam olmalı.")]
    public string Number { get; set; } = string.Empty;

    [Range(1, 12)]
    public int ExpMonth { get; set; }

    [Range(2000, 2200)]
    public int ExpYear { get; set; }

    [Required, RegularExpression(@"^\d{3,4}$", ErrorMessage = "CVV 3 veya 4 haneli rakam olmalı.")]
    public string Cvv { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string HolderName { get; set; } = string.Empty;

    public override string ToString() => "CardDto(***)";
}
