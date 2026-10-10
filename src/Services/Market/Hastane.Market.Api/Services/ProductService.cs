using Hastane.Market.Api.Caching;
using Hastane.Market.Api.Data;
using Hastane.Market.Api.Dtos;
using Hastane.Market.Api.Entities;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Market.Api.Services;

public class ProductService(MarketDbContext db, IMapper mapper, IProductCache cache) : IProductService
{
    public async Task<OperationResult<PagedResult<ResultProductDto>>> GetAllAsync(ProductListQuery query, bool isAdmin, CancellationToken ct)
    {
        ProductCategory? category = null;
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            if (!ProductCategories.TryParse(query.Category, out var parsed))
                return OperationResult<PagedResult<ResultProductDto>>.Validation(
                    $"Geçersiz kategori. Geçerli değerler: {string.Join(", ", Enum.GetNames<ProductCategory>())}.");
            category = parsed;
        }

        // Pasifleri yalnız Admin görür; yönetici görünümü kişiye özel olduğundan önbelleğe girmez.
        var includeInactive = isAdmin && query.IncludeInactive;
        var cacheKey = ProductCache.ListKey(query.Search, category?.ToString(), query.Page, query.PageSize);

        if (!includeInactive)
        {
            var cached = await cache.GetAsync<PagedResult<ResultProductDto>>(cacheKey, ct);
            if (cached is not null) return OperationResult<PagedResult<ResultProductDto>>.Ok(cached);
        }

        var products = db.Products.AsNoTracking().AsQueryable();
        if (!includeInactive) products = products.Where(p => p.IsActive);
        if (category is not null) products = products.Where(p => p.Category == category);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Contains -> LIKE '%...%'; EF % ve _ karakterlerini kaçırır, kullanıcı joker kullanamaz.
            var term = query.Search.Trim();
            products = products.Where(p => p.Name.Contains(term) || p.Description.Contains(term));
        }

        var total = await products.CountAsync(ct);
        var items = await products
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(ct);

        var result = new PagedResult<ResultProductDto>
        {
            Items = items.Select(p => mapper.Map<ResultProductDto>(p)).ToList(),
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        };

        if (!includeInactive) await cache.SetAsync(cacheKey, result, ct);
        return OperationResult<PagedResult<ResultProductDto>>.Ok(result);
    }

    public async Task<ResultProductDto?> GetByIdAsync(Guid id, bool isAdmin, CancellationToken ct)
    {
        if (!isAdmin)
        {
            var cached = await cache.GetAsync<ResultProductDto>(ProductCache.ProductKey(id), ct);
            if (cached is not null) return cached;
        }

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        // Pasif ürün Admin dışında "yok" gibi davranır; var olduğu bilgisi sızmaz.
        if (product is null || (!product.IsActive && !isAdmin)) return null;

        var dto = mapper.Map<ResultProductDto>(product);
        if (!isAdmin) await cache.SetAsync(ProductCache.ProductKey(id), dto, ct);
        return dto;
    }

    public async Task<OperationResult<ResultProductDto>> CreateAsync(CreateProductDto dto, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var product = new Product
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = now,
        };
        Apply(product, dto, now);

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAsync([product.Id], ct);
        return OperationResult<ResultProductDto>.Created(mapper.Map<ResultProductDto>(product));
    }

    public async Task<OperationResult<ResultProductDto>> UpdateAsync(Guid id, UpdateProductDto dto, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return OperationResult<ResultProductDto>.NotFound();

        Apply(product, dto, DateTime.UtcNow);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Okuma ile yazma arasında stok düştü (rezervasyon) ya da ürün silindi: yönetici güncel haliyle yeniden denemeli.
            return OperationResult<ResultProductDto>.Conflict("Ürün başka bir işlemle değişti (stok düşmüş olabilir). Sayfayı yenileyip tekrar deneyin.");
        }

        await cache.InvalidateAsync([id], ct);
        return OperationResult<ResultProductDto>.Ok(mapper.Map<ResultProductDto>(product));
    }

    public async Task<OperationResult<bool>> DeleteAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return OperationResult<bool>.NotFound();

        db.Products.Remove(product);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult<bool>.Conflict("Ürün başka bir işlemle değişti. Sayfayı yenileyip tekrar deneyin.");
        }

        await cache.InvalidateAsync([id], ct);
        return OperationResult<bool>.Ok(true);
    }

    private static void Apply(Product product, CreateProductDto dto, DateTime now)
    {
        product.Name = dto.Name.Trim();
        product.Description = dto.Description?.Trim() ?? string.Empty;
        product.Category = dto.Category!.Value;
        product.Price = dto.Price;
        product.Currency = ProductCurrency.Try;
        product.Stock = dto.Stock;
        product.ImageUrl = string.IsNullOrWhiteSpace(dto.ImageUrl) ? null : dto.ImageUrl.Trim();
        product.IsActive = dto.IsActive;
        product.UpdatedAt = now;
    }
}
