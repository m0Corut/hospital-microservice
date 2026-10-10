using System.ComponentModel.DataAnnotations;
using Hastane.Order.Api.Entities;

namespace Hastane.Order.Api.Dtos;

// Hasta kimliği/adı gövdeden ALINMAZ: token'dan gelir. Fiyat da gövdede yok: sunucu Market'ten okur.
public class CheckoutDto
{
    [Required, StringLength(300, MinimumLength = 5)]
    public string ShippingAddress { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Note { get; set; }
}

public class UpdateOrderStatusDto
{
    [Required]
    public OrderStatus? Status { get; set; }
}

public class CancelOrderDto
{
    [StringLength(300)]
    public string? Reason { get; set; }
}

public class OrderListQuery
{
    public OrderStatus? Status { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize { get; set; } = 10;
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

// Liste satırı.
public class ResultOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PatientUserId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OrderItemDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

// Ayrıntı.
public class GetByIdOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string PatientUserId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public string? Note { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CancelReason { get; set; }
    public bool StockReleasePending { get; set; }
    public List<OrderItemDto> Items { get; set; } = [];
}
