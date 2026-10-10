namespace Hastane.Order.Api.Entities;

// Sipariş anındaki değerlerin kopyası: ürünün fiyatı/adı sonradan değişse de sipariş değişmez.
public class OrderItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}
