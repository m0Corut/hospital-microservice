namespace Hastane.Order.Api.Entities;

public enum OrderStatus
{
    Received,
    Preparing,
    Shipped,
    Delivered,
    Cancelled,
}

public class Order
{
    // Ödeme yöntemi şimdilik tek: kapıda ödeme (gerçek ödeme yok).
    public const string CashOnDelivery = "CashOnDelivery";

    public Guid Id { get; set; } = Guid.CreateVersion7();

    // Okunur numara: SIP-2026-000123. Yıl bazlı sayaç tablosundan (OrderCounter) üretilir.
    public string OrderNumber { get; set; } = string.Empty;

    public string PatientUserId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public string? Note { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Received;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "TRY";
    public string PaymentMethod { get; set; } = CashOnDelivery;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CancelReason { get; set; }

    // Çift tıklama savunması: aynı hasta + aynı anahtar = aynı sipariş (tekil indeks).
    public string? IdempotencyKey { get; set; }

    // ESKİ (senkron release dönemi): iptal edildi ama Market'e iade çağrısı ulaşmadı. Artık iade OrderCancelled olayıyla (outbox)
    // yapıldığından hep false kalır; sütun/DTO alanı geriye dönük uyum için durur.
    public bool StockReleasePending { get; set; }

    // İyimser eşzamanlılık: her değişiklikte servis 1 artırır, EF "UPDATE ... WHERE Version = okuduğum" yazar.
    // İki yönetici/hasta aynı siparişi aynı anda değiştirirse ikincisi sessizce ezmez, 409 alır.
    public int Version { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}
