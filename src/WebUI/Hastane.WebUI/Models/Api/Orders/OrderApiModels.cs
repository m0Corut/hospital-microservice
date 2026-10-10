namespace Hastane.WebUI.Models.Api.Orders;

// Order servisi JSON'unun WebUI kopyası. Status metin gelir: Received / Preparing / Shipped / Delivered / Cancelled.

public sealed class OrderPagedApiModel<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// Liste satırı (GET /gateway/orders).
public sealed class OrderListItemApiModel
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PatientUserId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class OrderItemApiModel
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

// Ayrıntı (GET /gateway/orders/{id}) ve checkout/iptal/durum yanıtı.
public sealed class OrderDetailApiModel
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PatientUserId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CancelReason { get; set; }
    public bool StockReleasePending { get; set; }
    public List<OrderItemApiModel> Items { get; set; } = [];
}

// Checkout ekranının sepet özeti (GET /gateway/cart: Market servisi). Fiyatlar yalnız GÖSTERİM içindir;
// sipariş tutarını Order servisi ürün servisinden yeniden okuyup kendisi hesaplar.
public sealed class CheckoutCartApiModel
{
    public List<CheckoutCartItemApiModel> Items { get; set; } = [];
    public decimal Total { get; set; }
    public string Currency { get; set; } = "TRY";
}

public sealed class CheckoutCartItemApiModel
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

// Gövdede hasta kimliği, fiyat, durum YOK: hepsi sunucu alanıdır.
public sealed record CheckoutRequest(string ShippingAddress, string? Note);

public sealed record UpdateOrderStatusRequest(string Status);

public sealed record CancelOrderRequest(string? Reason);
