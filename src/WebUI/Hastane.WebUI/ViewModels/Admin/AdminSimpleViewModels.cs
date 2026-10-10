using System.ComponentModel.DataAnnotations;
using Hastane.WebUI.Models.Api.Admin;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Hastane.WebUI.ViewModels.Admin;

public sealed class BranchFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Branş adı gerekli."), StringLength(100, ErrorMessage = "Branş adı en çok 100 karakter olabilir.")]
    [Display(Name = "Branş adı")]
    public string? Name { get; set; }

    [Display(Name = "Açıklama")]
    public string? Description { get; set; }

    [Display(Name = "Simge (kısa metin)")]
    public string? Icon { get; set; }
}

public sealed class RoleCreateViewModel
{
    [Required(ErrorMessage = "Rol adı gerekli."), StringLength(50, ErrorMessage = "Rol adı en çok 50 karakter olabilir.")]
    [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Rol adı yalnız harf ve rakam içerebilir.")]
    [Display(Name = "Rol adı")]
    public string? Name { get; set; }
}

public sealed class RoleIndexViewModel
{
    public List<AdminRoleApiModel> Roles { get; init; } = [];
    public RoleCreateViewModel NewRole { get; init; } = new();
}

public sealed class UserIndexViewModel
{
    public string? Search { get; init; }
    public string? Role { get; init; }
    public required AdminPagedApiModel<AdminUserApiModel> Page { get; init; }
    public List<string> Roles { get; init; } = [];
}

public sealed class UserDetailsViewModel
{
    public required AdminUserApiModel User { get; init; }
    public List<string> AllRoles { get; init; } = [];
    public List<SelectListItem> DoctorChoices { get; init; } = [];
    public string? LinkedDoctorName { get; init; }
}

public sealed class AppointmentFilterViewModel
{
    public string? DoctorId { get; set; }
    public string? PatientId { get; set; }
    public string? Status { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
}

public sealed class AppointmentIndexViewModel
{
    public required AppointmentFilterViewModel Filter { get; init; }
    public List<AdminAppointmentApiModel> Items { get; init; } = [];
    public required AdminLookups Lookups { get; init; }
}

public sealed class AppointmentDetailsViewModel
{
    public required AdminAppointmentApiModel Appointment { get; init; }
    public required AdminLookups Lookups { get; init; }
}

public sealed class PaymentFilterViewModel
{
    public string? Status { get; set; }
    public string? PatientId { get; set; }
    public string? DoctorId { get; set; }
}

public sealed class PaymentIndexViewModel
{
    public required PaymentFilterViewModel Filter { get; init; }
    public required AdminPagedApiModel<AdminPaymentApiModel> Page { get; init; }
    public required AdminLookups Lookups { get; init; }
}

public sealed class PaymentDetailsViewModel
{
    public required AdminPaymentApiModel Payment { get; init; }
    public required AdminLookups Lookups { get; init; }
}

public sealed class InvoiceFilterViewModel
{
    public string? PatientId { get; set; }
    public string? DoctorId { get; set; }
}

public sealed class InvoiceIndexViewModel
{
    public required InvoiceFilterViewModel Filter { get; init; }
    public required AdminPagedApiModel<AdminInvoiceApiModel> Page { get; init; }
    public required AdminLookups Lookups { get; init; }
}

public sealed class InvoiceDetailsViewModel
{
    public required AdminInvoiceApiModel Invoice { get; init; }
    public required AdminLookups Lookups { get; init; }
}

public sealed class PrescriptionFilterViewModel
{
    public string? PatientId { get; set; }
    public string? DoctorId { get; set; }
    public string? AppointmentId { get; set; }
}

public sealed class PrescriptionIndexViewModel
{
    public required PrescriptionFilterViewModel Filter { get; init; }
    public List<AdminPrescriptionListItemApiModel> Items { get; init; } = [];
    public required AdminLookups Lookups { get; init; }
}

public sealed class PrescriptionDetailsViewModel
{
    public required AdminPrescriptionApiModel Prescription { get; init; }
    public required AdminLookups Lookups { get; init; }
}

public sealed class DoctorIndexViewModel
{
    public string? Text { get; init; }
    public string? BranchId { get; init; }
    public required AdminPagedApiModel<AdminDoctorListItemApiModel> Page { get; init; }
    public Dictionary<string, string> BranchNames { get; init; } = [];
    public List<SelectListItem> BranchChoices { get; init; } = [];
}
