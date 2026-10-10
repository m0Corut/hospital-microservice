using System.ComponentModel.DataAnnotations;
using Hastane.Payment.Api.Validation;

namespace Hastane.Payment.Api.Dtos;

// Id, Status, tarihler, fatura ve sağlayıcı bilgileri bilerek yok: bunları sunucu atar.
public class CreatePaymentDto
{
    [NotEmptyGuid]
    public Guid AppointmentId { get; set; }

    // Hasta (Patient rolü) için YOK SAYILIR: PatientId token'daki sub'dan gelir (başkası adına ödeme açılamaz).
    // Admin için de isteğe bağlı: asıl hasta RANDEVUDAN gelir; doldurulursa randevununkiyle uyuşmalı (yoksa 400).
    [StringLength(64)]
    public string? PatientId { get; set; }

    // Geriye uyumluluk için DTO'da kaldı ama ARTIK ZORUNLU DEĞİL ve güvenilmez: doktor randevudan alınır.
    // Gönderilirse randevunun doktoruyla uyuşmalı (yoksa 400).
    [StringLength(64)]
    public string? DoctorId { get; set; }

    // İstemcinin GÖRDÜĞÜ tutar: sunucunun hesapladığı güncel ücretle birebir eşleşmezse 409. Faturaya hep sunucu değeri yazılır.
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
