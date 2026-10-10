using System.ComponentModel.DataAnnotations;
using Hastane.Market.Api.Entities;
using Hastane.Market.Api.Validation;

namespace Hastane.Market.Api.Dtos;

// Id, tarihler ve RowVersion bilerek yok: bunları sunucu atar.
public class CreateProductDto
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    // Nullable + Required: alan hiç gönderilmezse enum'un varsayılanına sessizce düşmesin.
    [Required]
    public ProductCategory? Category { get; set; }

    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [MaxDecimalPlaces(2)]
    public decimal Price { get; set; }

    // Yalnız TRY (sözleşme). Boş bırakılırsa TRY.
    [RegularExpression("^TRY$", ErrorMessage = "Para birimi TRY olmalı.")]
    public string? Currency { get; set; }

    [Range(0, 100000)]
    public int Stock { get; set; }

    [ProductImageUrl, StringLength(300)]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;
}

// PUT tam değiştirme: aynı alanlar.
public class UpdateProductDto : CreateProductDto
{
}

public class ResultProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = ProductCurrency.Try;
    public int Stock { get; set; }
    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; }
}

public class ProductListQuery
{
    [StringLength(100)]
    public string? Search { get; set; }

    // Ad olarak (Vitamin, Care, MedicalDevice, MotherBaby, FirstAid); geçersizse 400.
    [StringLength(20)]
    public string? Category { get; set; }

    [Range(1, 100000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 12;

    // Yalnız Admin için anlamlı: pasif ürünleri de getirir. Diğerlerinde yok sayılır.
    public bool IncludeInactive { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
