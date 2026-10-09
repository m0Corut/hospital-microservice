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
