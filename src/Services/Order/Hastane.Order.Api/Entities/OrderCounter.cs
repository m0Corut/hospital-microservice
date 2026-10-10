namespace Hastane.Order.Api.Entities;

// Yıl başına tek satırlık sayaç: sipariş numarasının sıra kısmı. Bkz. OrderNumberGenerator.
public class OrderCounter
{
    public int Year { get; set; }
    public int LastValue { get; set; }
}
