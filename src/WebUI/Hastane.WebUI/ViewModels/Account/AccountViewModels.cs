using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

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

    // Yalnız gösterim: mevcut fotoğrafın WebUI adresi (/media/users/{id}/photo) ve ad (baş harf avatarı için).
    public string? PhotoUrl { get; set; }
    public string DisplayName => $"{FirstName} {LastName}".Trim();
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

public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "E-posta gerekli."), EmailAddress(ErrorMessage = "Geçerli bir e-posta girin."), StringLength(256)]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = string.Empty;
}

// Belirteç ve e-posta bağlantıdan gelir, formda gizli alanda taşınır; sayfada görünür yazılmaz.
public sealed class ResetPasswordViewModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(2048)]
    public string Token { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni şifre gerekli."), StringLength(128, MinimumLength = 8, ErrorMessage = "Şifre en az 8 karakter olmalı.")]
    [DataType(DataType.Password), Display(Name = "Yeni şifre")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni şifre tekrarı gerekli."), Compare(nameof(NewPassword), ErrorMessage = "Şifreler aynı değil.")]
    [DataType(DataType.Password), Display(Name = "Yeni şifre (tekrar)")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class RegisterDoctorViewModel
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

    [Required(ErrorMessage = "Branş seçin."), StringLength(64)]
    [Display(Name = "Branş")]
    public string? BranchId { get; set; }

    [Required(ErrorMessage = "Lisans numarası gerekli."), StringLength(50, ErrorMessage = "Lisans numarası en çok 50 karakter olabilir.")]
    [Display(Name = "Lisans numarası")]
    public string LicenseNumber { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Not en çok 500 karakter olabilir.")]
    [Display(Name = "Not (isteğe bağlı)")]
    public string? Note { get; set; }

    // Formdan alınmaz; her görüntülemede /gateway/branches'tan doldurulur.
    [BindNever, ValidateNever]
    public List<SelectListItem> Branches { get; set; } = [];

    [BindNever, ValidateNever]
    public bool BranchesUnavailable { get; set; }
}

// Giriş yapmış kullanıcının kendi başvurusu (yoksa Application null).
public sealed class DoctorApplicationStatusViewModel
{
    public Hastane.WebUI.Models.Api.DoctorApplicationApiModel? Application { get; init; }
    public string? BranchName { get; init; }

    public static string StatusText(string status) => status switch
    {
        "Pending" => "Bekliyor",
        "Approved" => "Onaylandı",
        "Rejected" => "Reddedildi",
        _ => status,
    };

    public static string StatusBadge(string status) => status switch
    {
        "Approved" => "text-bg-success",
        "Rejected" => "text-bg-danger",
        _ => "text-bg-warning",
    };
}
