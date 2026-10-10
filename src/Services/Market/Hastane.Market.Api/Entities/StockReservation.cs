namespace Hastane.Market.Api.Entities;

public enum ReservationStatus
{
    Reserved = 1,
    Released = 2,
}

// Bir siparişin stok rezervasyonu. OrderId tekildir: aynı sipariş ikinci kez rezerve edilemez (idempotency anahtarı).
public class StockReservation
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public ReservationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public List<StockReservationItem> Items { get; set; } = [];
}

// Ürüne yabancı anahtar BİLEREK yok: ürün sonradan silinse bile rezervasyon kaydı (denetim izi) kalır.
public class StockReservationItem
{
    public int Id { get; set; }
    public Guid ReservationId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}
