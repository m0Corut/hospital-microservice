using System.ComponentModel.DataAnnotations;

namespace Hastane.WebUI.ViewModels.Account;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "E-posta gerekli."), EmailAddress(ErrorMessage = "Geçerli bir e-posta girin."), StringLength(256)]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre gerekli."), StringLength(128)]
    [DataType(DataType.Password), Display(Name = "Şifre")]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Ad gerekli."), StringLength(100)]
    [Display(Name = "Ad")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Soyad gerekli."), StringLength(100)]
    [Display(Name = "Soyad")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-posta gerekli."), EmailAddress(ErrorMessage = "Geçerli bir e-posta girin."), StringLength(256)]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre gerekli."), StringLength(128, MinimumLength = 8, ErrorMessage = "Şifre en az 8 karakter olmalı.")]
    [DataType(DataType.Password), Display(Name = "Şifre")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre tekrarı gerekli."), Compare(nameof(Password), ErrorMessage = "Şifreler aynı değil.")]
    [DataType(DataType.Password), Display(Name = "Şifre (tekrar)")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

// Hesap ayarları: yalnız ad ve soyad düzenlenir; e-posta ve roller salt okunur (API de bu ikisini değiştirmez).
public sealed class ProfileViewModel
{
    [Required(ErrorMessage = "Ad gerekli."), StringLength(100, ErrorMessage = "Ad en çok 100 karakter olabilir.")]
    [Display(Name = "Ad")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Soyad gerekli."), StringLength(100, ErrorMessage = "Soyad en çok 100 karakter olabilir.")]
    [Display(Name = "Soyad")]
    public string LastName { get; set; } = string.Empty;

    // Yalnız gösterim (formdan alınmaz, her istekte API'den okunur).
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Mevcut şifre gerekli."), StringLength(128)]
    [DataType(DataType.Password), Display(Name = "Mevcut şifre")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni şifre gerekli."), StringLength(128, MinimumLength = 8, ErrorMessage = "Şifre en az 8 karakter olmalı.")]
    [DataType(DataType.Password), Display(Name = "Yeni şifre")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni şifre tekrarı gerekli."), Compare(nameof(NewPassword), ErrorMessage = "Şifreler aynı değil.")]
    [DataType(DataType.Password), Display(Name = "Yeni şifre (tekrar)")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
