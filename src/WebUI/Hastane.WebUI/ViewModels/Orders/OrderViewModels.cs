using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Hastane.WebUI.Models.Api.Orders;
using Hastane.WebUI.ViewModels.Admin;

namespace Hastane.WebUI.ViewModels.Orders;

// Checkout formu. Fiyat, hasta kimliği, durum formda YOK; sepet özeti yalnız gösterimdir (sunucu fiyatı yeniden okur).
public class CheckoutViewModel
{
    [Required(ErrorMessage = "Teslimat adresi zorunlu.")]
    [StringLength(300, MinimumLength = 5, ErrorMessage = "Teslimat adresi 5-300 karakter olmalı.")]
    [Display(Name = "Teslimat adresi")]
    public string? ShippingAddress { get; set; }

    [StringLength(500, ErrorMessage = "Not en fazla 500 karakter olabilir.")]
    [Display(Name = "Sipariş notu")]
    public string? Note { get; set; }

    // Form her açıldığında BİR kez üretilir, gizli alanla taşınır: çift tıklama aynı anahtarı yollar.
    public string? IdempotencyKey { get; set; }

    // --- yalnız gösterim (formdan bağlanmaz, her yanıtta yeniden okunur) ---
    // [BindNever]: form alanı olarak gelseler bile bağlanmaz (kurcalanmış "Cart.Total" gibi alanlar yok sayılır).
    [BindNever]
    public CheckoutCartApiModel Cart { get; set; } = new();

    [BindNever]
    public bool CartUnavailable { get; set; }

    [BindNever]
    public bool AddressPrefilled { get; set; }
}

public sealed class PatientOrderListViewModel
{
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public List<OrderListItemApiModel> Items { get; init; } = [];
}

public sealed class OrderDetailViewModel
{
    public OrderDetailApiModel Order { get; init; } = new();

    // Butonlar yalnız kolaylıktır; asıl kural API'dedir (görünüm güvenlik değildir).
    public bool CanCancel { get; init; }
}

public sealed class AdminOrderFilterViewModel
{
    public string? Status { get; set; }
}

public sealed class AdminOrderIndexViewModel
{
    public AdminOrderFilterViewModel Filter { get; init; } = new();
    public OrderPagedApiModel<OrderListItemApiModel> Page { get; init; } = new();
}

public sealed class AdminOrderDetailViewModel
{
    public OrderDetailApiModel Order { get; init; } = new();
    public IReadOnlyList<string> NextStatuses { get; init; } = [];
    public bool CanCancel { get; init; }
}

public sealed class CancelOrderForm
{
    [StringLength(300, ErrorMessage = "İptal nedeni en fazla 300 karakter olabilir.")]
    [Display(Name = "İptal nedeni")]
    public string? Reason { get; set; }
}

// Sipariş ekranlarının ortak biçimlendirmesi. Durum adları API ile birebir; ekran metni Türkçe.
public static class OrderDisplay
{
    public static readonly string[] Statuses = ["Received", "Preparing", "Shipped", "Delivered", "Cancelled"];

    public static string Status(string status) => status switch
    {
        "Received" => "Alındı",
        "Preparing" => "Hazırlanıyor",
        "Shipped" => "Kargoda",
        "Delivered" => "Teslim edildi",
        "Cancelled" => "İptal edildi",
        _ => status,
    };

    public static string Badge(string status) => status switch
    {
        "Delivered" => "text-bg-success",
        "Cancelled" => "text-bg-danger",
        "Shipped" => "text-bg-primary",
        "Preparing" => "text-bg-info",
        _ => "text-bg-warning",
    };

    public static string PaymentMethod(string method) => method switch
    {
        "CashOnDelivery" => "Kapıda ödeme",
        _ => method,
    };

    // Admin için sonraki geçerli durumlar (API durum makinesinin aynası; kural orada, burası yalnız düğmeleri çizer).
    public static IReadOnlyList<string> NextStatuses(string current) => current switch
    {
        "Received" => ["Preparing"],
        "Preparing" => ["Shipped"],
        "Shipped" => ["Delivered"],
        _ => [],
    };

    public static string StatusAction(string target) => target switch
    {
        "Preparing" => "Hazırlanıyor yap",
        "Shipped" => "Kargoya verildi",
        "Delivered" => "Teslim edildi",
        _ => Status(target),
    };

    public static bool PatientCanCancel(string status) => status == "Received";

    public static bool AdminCanCancel(string status) => status is "Received" or "Preparing";

    public static string Money(decimal amount, string currency) => AdminDisplay.Money(amount, currency);
}
