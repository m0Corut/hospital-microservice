using System.ComponentModel.DataAnnotations;

namespace Hastane.WebUI.ViewModels.Home;

// İletişim formu: eğitim projesi, hiçbir yere gönderilmez ve kaydedilmez.
public sealed class ContactFormViewModel
{
    [Required(ErrorMessage = "Adınızı yazın."), StringLength(100, ErrorMessage = "Ad en çok 100 karakter olabilir.")]
    [Display(Name = "Ad soyad")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-posta adresinizi yazın."), EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi yazın."), StringLength(200)]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Konu yazın."), StringLength(150, ErrorMessage = "Konu en çok 150 karakter olabilir.")]
    [Display(Name = "Konu")]
    public string Subject { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mesajınızı yazın."), StringLength(2000, ErrorMessage = "Mesaj en çok 2000 karakter olabilir.")]
    [Display(Name = "Mesaj")]
    public string Message { get; set; } = string.Empty;
}
