using Hastane.Order.Api.Data;
using Hastane.Order.Api.Dtos;
using Hastane.Order.Api.Entities;
using Hastane.Order.Api.Integration;
using Hastane.Shared.Security;
using Mapster;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using OrderEntity = Hastane.Order.Api.Entities.Order;

namespace Hastane.Order.Api.Services;

public class OrderService : IOrderService
{
    public const string EmptyCartMessage = "Sepetiniz boş.";
    public const string InsufficientStockReason = "Stok yetersiz";
    public const string ReserveUnavailableReason = "Stok ayrılamadı (Market yanıt vermedi)";
    public const string MarketUnavailableMessage = "Ürün servisine ulaşılamadı, lütfen biraz sonra tekrar deneyin.";
    public const string ConcurrentChangeMessage = "Sipariş başka bir işlemle değiştirildi, sayfayı yenileyip tekrar deneyin.";

    private readonly OrderDbContext _db;
    private readonly IMarketClient _market;
    private readonly ICurrentUser _user;
    private readonly ILogger<OrderService> _logger;

    public OrderService(OrderDbContext db, IMarketClient market, ICurrentUser user, ILogger<OrderService> logger)
    {
        _db = db;
        _market = market;
        _user = user;
        _logger = logger;
    }

    public async Task<OperationResult<GetByIdOrderDto>> CheckoutAsync(
        string? idempotencyKey, CheckoutDto dto, string patientName, CancellationToken ct)
    {
        var userId = _user.UserId ?? throw new InvalidOperationException("Kimliksiz kullanıcı checkout'a ulaşamaz.");

        // 1) Çift tıklama: aynı anahtar daha önce işlendiyse AYNI sipariş (yeni sipariş, yeni stok düşümü yok).
        if (idempotencyKey is not null)
        {
            var previous = await FindByKeyAsync(userId, idempotencyKey, ct);
            if (previous is not null) return Replay(previous);
        }

        // 2) Sepet (kullanıcının kendi token'ıyla Market'ten).
        var cartResult = await _market.GetCartAsync(ct);
        if (cartResult.Outcome == MarketOutcome.Unavailable) return OperationResult<GetByIdOrderDto>.ServiceUnavailable(MarketUnavailableMessage);
        var cartLines = (cartResult.Value?.Items ?? [])
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(i => i.Quantity)))
            .ToList();
        if (cartLines.Count == 0)
        {
            // Çift tıklamanın yavaş kalan ikinci isteği: birinci istek siparişi yazıp sepeti boşaltmış olabilir.
            // "Sepetiniz boş" demeden önce anahtarı bir kez daha ara; sipariş varsa onu döndür.
            if (idempotencyKey is not null)
            {
                var winner = await FindByKeyAsync(userId, idempotencyKey, ct);
                if (winner is not null) return Replay(winner);
            }
            return OperationResult<GetByIdOrderDto>.Validation(EmptyCartMessage);
        }

        // 3) Fiyat ve aktiflik SUNUCU doğrusundan: sepetteki fiyat yok sayılır (istemci/önbellek eski ya da oynanmış olabilir).
        var items = new List<OrderItem>();
        string? currency = null;
        foreach (var (productId, quantity) in cartLines)
        {
            var productResult = await _market.GetProductAsync(productId, ct);
            if (productResult.Outcome == MarketOutcome.Unavailable) return OperationResult<GetByIdOrderDto>.ServiceUnavailable(MarketUnavailableMessage);
            if (productResult.Outcome == MarketOutcome.NotFound || productResult.Value is not { IsActive: true } product)
                return OperationResult<GetByIdOrderDto>.Conflict("Sepetinizdeki bir ürün artık satışta değil. Sepeti güncelleyip tekrar deneyin.");

            currency ??= product.Currency;
            if (!string.Equals(currency, product.Currency, StringComparison.OrdinalIgnoreCase))
                return OperationResult<GetByIdOrderDto>.Conflict("Sepetteki ürünler farklı para birimlerinde, tek siparişte verilemez.");

            var unitPrice = Money(product.Price);
            items.Add(new OrderItem
            {
                ProductId = productId,
                ProductName = Truncate(product.Name, 200),
                UnitPrice = unitPrice,
                Quantity = quantity,
                LineTotal = Money(unitPrice * quantity),
            });
        }

        var order = new OrderEntity
        {
            PatientUserId = userId,
            PatientName = Truncate(string.IsNullOrWhiteSpace(patientName) ? userId : patientName, 200),
            ShippingAddress = dto.ShippingAddress.Trim(),
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            Status = OrderStatus.Received,
            Currency = currency!.ToUpperInvariant(),
            PaymentMethod = OrderEntity.CashOnDelivery,
            IdempotencyKey = idempotencyKey,
            Items = items,
            TotalAmount = Money(items.Sum(i => i.LineTotal)),
        };

        // 4) Siparişi `Received` olarak yaz (numara aynı işlemde üretilir).
        try
        {
            await InsertWithNumberAsync(order, ct);
        }
        catch (DbUpdateException ex) when (idempotencyKey is not null && IsUniqueViolation(ex))
        {
            // Aynı anahtarlı ikinci istek birinciyle yarıştı ve tekil indeks onu geri çevirdi: birincinin siparişini döndür.
            _db.ChangeTracker.Clear();
            var winner = await FindByKeyAsync(userId, idempotencyKey, CancellationToken.None);
            if (winner is not null) return Replay(winner);
            throw;
        }

        // Bu noktadan sonra sipariş KAYITLI: istemci vazgeçse de (ct) telafi adımları yarım kalmasın diye CancellationToken.None.
        // 5) Stok ayır. Başarısız yolların hepsi siparişi Cancelled'a çeker: "Received ama stok düşmemiş" sipariş kalmaz.
        var outcome = await _market.ReserveAsync(
            order.Id, order.Items.Select(i => new ReserveItem(i.ProductId, i.Quantity)).ToList(), CancellationToken.None);

        switch (outcome)
        {
            case ReserveOutcome.InsufficientStock:
                // Market hiçbir şey düşmedi (atomik): iade çağrısı gereksiz.
                await MarkCancelledAsync(order, InsufficientStockReason, release: false);
                return OperationResult<GetByIdOrderDto>.Conflict("Stok yetersiz. Sepetinizdeki miktarları azaltıp tekrar deneyin.");

            case ReserveOutcome.Unavailable:
                // Karar: Market kapalı/zaman aşımı => sipariş Cancelled (silinmez, iz kalır) ve 503. Yanıt kaybolmuş olabilir
                // (Market stoğu düşmüş ama biz duymamış), bu yüzden idempotent release da denenir; ulaşamazsak işaret kalır.
                await MarkCancelledAsync(order, ReserveUnavailableReason, release: true);
                return OperationResult<GetByIdOrderDto>.ServiceUnavailable(MarketUnavailableMessage);
        }

        // 6) Sepeti boşalt. Başarısızsa sipariş yine başarılı: stok düştü, sipariş yazıldı; hasta sepeti elle boşaltır.
        if (!await _market.ClearCartAsync(CancellationToken.None))
            _logger.LogWarning("Sipariş {OrderId} oluştu ama sepet boşaltılamadı.", order.Id);

        return OperationResult<GetByIdOrderDto>.Created(order.Adapt<GetByIdOrderDto>());
    }

    public async Task<PagedResult<ResultOrderDto>> GetAllAsync(OrderListQuery query, CancellationToken ct)
    {
        var orders = _db.Orders.AsNoTracking().AsQueryable();
        if (!_user.IsAdmin) orders = orders.Where(o => o.PatientUserId == _user.UserId);
        if (query.Status is { } status) orders = orders.Where(o => o.Status == status);

        var total = await orders.CountAsync(ct);
        var page = await orders
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<ResultOrderDto>
        {
            Items = page.Adapt<List<ResultOrderDto>>(),
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    public async Task<GetByIdOrderDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var order = await _db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);
        return order is null || !CanSee(order) ? null : order.Adapt<GetByIdOrderDto>();
    }

    public async Task<OperationResult<GetByIdOrderDto>> UpdateStatusAsync(Guid id, OrderStatus target, CancellationToken ct)
    {
        var order = await LoadAsync(id, ct);
        if (order is null) return OperationResult<GetByIdOrderDto>.NotFound();

        if (!OrderStatusTransitions.IsAllowed(order.Status, target))
            return OperationResult<GetByIdOrderDto>.Conflict($"Sipariş '{order.Status}' durumundan '{target}' durumuna geçirilemez.");

        try
        {
            if (target == OrderStatus.Cancelled)
            {
                await MarkCancelledAsync(order, "Yönetici tarafından iptal edildi", release: true);
            }
            else
            {
                order.Status = target;
                Touch(order);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult<GetByIdOrderDto>.Conflict(ConcurrentChangeMessage);
        }

        return OperationResult<GetByIdOrderDto>.Ok(order.Adapt<GetByIdOrderDto>());
    }

    public async Task<OperationResult<GetByIdOrderDto>> CancelAsync(Guid id, string? reason, CancellationToken ct)
    {
        var order = await LoadAsync(id, ct);
        if (order is null) return OperationResult<GetByIdOrderDto>.NotFound();

        // Admin her zaman Received/Preparing iptal edebilir; hasta yalnız kendi siparişini ve yalnız Received iken.
        var allowed = _user.IsAdmin
            ? OrderStatusTransitions.AdminCanCancel(order.Status)
            : OrderStatusTransitions.PatientCanCancel(order.Status);
        if (!allowed)
            return OperationResult<GetByIdOrderDto>.Conflict(_user.IsAdmin
                ? $"'{order.Status}' durumundaki sipariş iptal edilemez."
                : "Sipariş hazırlanmaya başladığı için iptal edilemez.");

        var text = string.IsNullOrWhiteSpace(reason)
            ? (_user.IsAdmin ? "Yönetici tarafından iptal edildi" : "Hasta tarafından iptal edildi")
            : reason.Trim();

        try
        {
            await MarkCancelledAsync(order, text, release: true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OperationResult<GetByIdOrderDto>.Conflict(ConcurrentChangeMessage);
        }

        return OperationResult<GetByIdOrderDto>.Ok(order.Adapt<GetByIdOrderDto>());
    }

    // --- yardımcılar ---

    // Siparişi iptal eder; release ise Market'e stok iadesini söyler. Sıra bilinçli: ÖNCE iptal kaydedilir (iyimser
    // eşzamanlılık burada yarışı yakalar; kaybeden iade çağrısına hiç geçmez), SONRA iade denenir. İade çağrısı sürerken
    // süreç ölürse "iade bekliyor" işareti zaten yazılıdır. İade başarısızsa sipariş YİNE iptaldir (hastanın siparişi,
    // Market'in kapalı olmasına rehin kalmaz): işaret + log kalır.
    private async Task MarkCancelledAsync(OrderEntity order, string reason, bool release)
    {
        order.Status = OrderStatus.Cancelled;
        order.CancelReason = Truncate(reason, 300);
        order.StockReleasePending = release;
        Touch(order);
        await _db.SaveChangesAsync(CancellationToken.None);

        if (!release) return;

        if (await _market.ReleaseAsync(order.Id, CancellationToken.None))
        {
            order.StockReleasePending = false;
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        else
        {
            _logger.LogWarning("Sipariş {OrderId} iptal edildi ama stok iadesi Market'e ulaşmadı (iade bekliyor).", order.Id);
        }
    }
    private async Task InsertWithNumberAsync(OrderEntity order, CancellationToken ct)
    {
        var year = DateTime.UtcNow.Year;
        await OrderNumberGenerator.EnsureCounterRowAsync(_db, year, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        order.OrderNumber = await OrderNumberGenerator.NextAsync(_db, year, ct);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private Task<OrderEntity?> LoadAsync(Guid id, CancellationToken ct)
        => _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id && (_user.IsAdmin || o.PatientUserId == _user.UserId), ct);

    private Task<OrderEntity?> FindByKeyAsync(string userId, string key, CancellationToken ct)
        => _db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.PatientUserId == userId && o.IdempotencyKey == key, ct);

    // Aynı anahtarla tekrar gelen istek ilk isteğin sonucunu alır: başarılıysa 200 + aynı sipariş, ilk istek stok/Market
    // yüzünden iptal olmuşsa aynı hata (iptal edilmiş siparişi "başarı" gibi göstermeyiz).
    private static OperationResult<GetByIdOrderDto> Replay(OrderEntity previous)
    {
        if (previous.Status == OrderStatus.Cancelled && previous.CancelReason == InsufficientStockReason)
            return OperationResult<GetByIdOrderDto>.Conflict("Stok yetersiz. Sepetinizdeki miktarları azaltıp tekrar deneyin.");
        if (previous.Status == OrderStatus.Cancelled && previous.CancelReason == ReserveUnavailableReason)
            return OperationResult<GetByIdOrderDto>.ServiceUnavailable(MarketUnavailableMessage);
        return OperationResult<GetByIdOrderDto>.Ok(previous.Adapt<GetByIdOrderDto>());
    }

    private bool CanSee(OrderEntity order) => _user.IsAdmin || order.PatientUserId == _user.UserId;

    private static void Touch(OrderEntity order)
    {
        order.UpdatedAt = DateTime.UtcNow;
        order.Version++;
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    // MySQL/MariaDB 1062 = ER_DUP_ENTRY (tekil indeks ihlali).
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        for (Exception? e = ex.InnerException; e is not null; e = e.InnerException)
            if (e is MySqlException { Number: 1062 }) return true;
        return false;
    }
}
