using System.ComponentModel.DataAnnotations;

namespace Hastane.Market.Api.Dtos;

public class StockItemDto
{
    public Guid ProductId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public class ReserveStockDto
{
    public Guid OrderId { get; set; }

    [Required, MinLength(1), MaxLength(100)]
    public List<StockItemDto> Items { get; set; } = [];
}

public class ReleaseStockDto
{
    public Guid OrderId { get; set; }
}

public class ReservationResultDto
{
    public Guid OrderId { get; set; }

    // Reserved | Released | None (bu sipariş için rezervasyon yok; release için zararsız).
    public string Status { get; set; } = string.Empty;

    // true: istek daha önce işlenmişti, bu çağrıda stok değişmedi (idempotent tekrar).
    public bool Replayed { get; set; }

    public List<StockItemDto> Items { get; set; } = [];
}
