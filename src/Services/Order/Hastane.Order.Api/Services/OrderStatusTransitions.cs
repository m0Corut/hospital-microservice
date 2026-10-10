using Hastane.Order.Api.Entities;

namespace Hastane.Order.Api.Services;

// Durum makinesi tek yerde: Received -> Preparing -> Shipped -> Delivered; Received/Preparing -> Cancelled.
// Delivered ve Cancelled son durumdur. Aynı duruma geçiş de geçersizdir (sessizce "tamam" demek yerine 409).
public static class OrderStatusTransitions
{
    public static bool IsAllowed(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.Received, OrderStatus.Preparing) => true,
        (OrderStatus.Preparing, OrderStatus.Shipped) => true,
        (OrderStatus.Shipped, OrderStatus.Delivered) => true,
        (OrderStatus.Received, OrderStatus.Cancelled) => true,
        (OrderStatus.Preparing, OrderStatus.Cancelled) => true,
        _ => false,
    };

    // Hasta yalnız henüz hazırlanmaya başlamamış siparişi iptal edebilir.
    public static bool PatientCanCancel(OrderStatus status) => status == OrderStatus.Received;

    public static bool AdminCanCancel(OrderStatus status) => status is OrderStatus.Received or OrderStatus.Preparing;
}
