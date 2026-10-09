using System.ComponentModel.DataAnnotations;
using Hastane.Payment.Api.Validation;

namespace Hastane.Payment.Api.Dtos;

// Id, Status, tarihler, fatura ve sağlayıcı bilgileri bilerek yok: bunları sunucu atar.
public class CreatePaymentDto
{
    [NotEmptyGuid]
    public Guid AppointmentId { get; set; }

    // Hasta (Patient rolü) için YOK SAYILIR: PatientId token'daki sub'dan gelir (başkası adına ödeme açılamaz).
    // Yalnız Admin başkası adına ödeme alırken doldurur. DoctorId ise gövdeden gelir: hasta doktoru seçer.
    [StringLength(64)]
    public string? PatientId { get; set; }

    [Required, StringLength(64)]
    public string DoctorId { get; set; } = string.Empty;

    // decimal: para için double/float kullanılmaz. Üst sınır ve ondalık kuralı iki katmanda doğrulanır.
    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [MaxDecimalPlaces(2)]
    public decimal Amount { get; set; }

    // Biçim burada (3 büyük harf); hangi kodların kabul edildiği (TRY/USD/EUR) yapılandırmadan, serviste doğrulanır.
    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Para birimi 3 büyük harf olmalı.")]
    public string Currency { get; set; } = string.Empty;

    // Fatura kaleminin açıklaması; boşsa varsayılan metin yazılır.
    [StringLength(200)]
    public string? Description { get; set; }

    [Required]
    public CardDto Card { get; set; } = new();
}
