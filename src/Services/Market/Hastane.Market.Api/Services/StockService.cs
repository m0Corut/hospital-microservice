using Hastane.Market.Api.Caching;
using Hastane.Market.Api.Data;
using Hastane.Market.Api.Dtos;
using Hastane.Market.Api.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Market.Api.Services;

public enum ReserveOutcome
{
    Reserved,

    // Aynı orderId daha önce işlenmişti: stok bu çağrıda değişmedi, mevcut kayıt döner.
    Replayed,
    Invalid,
    Conflict,
}

// 409 gövdesi: hangi ürün neden yetmedi. Reason: InsufficientStock | ProductUnavailable | ProductNotFound.
public sealed record StockConflict(Guid ProductId, string Reason, int Requested, int Available);

public sealed record ReserveResult(ReserveOutcome Outcome, ReservationResultDto? Reservation = null, StockConflict? Conflict = null, string? Error = null);

public interface IStockService
{
    Task<ReserveResult> ReserveAsync(ReserveStockDto dto, CancellationToken ct);

    Task<OperationResult<ReservationResultDto>> ReleaseAsync(Guid orderId, CancellationToken ct);
}

public class StockService(MarketDbContext db, IProductCache cache) : IStockService
{
    public async Task<ReserveResult> ReserveAsync(ReserveStockDto dto, CancellationToken ct)
    {
        if (dto.OrderId == Guid.Empty) return Invalid("orderId boş olamaz.");
        if (dto.Items.Count == 0 || dto.Items.Any(i => i.ProductId == Guid.Empty || i.Quantity < 1))
            return Invalid("Her kalemde geçerli productId ve en az 1 adet olmalı.");

        // Aynı ürün iki satırda gelirse toplanır; ürün kimliğine göre sıralanır: eşzamanlı iki sipariş satırlara
        // hep aynı sırada dokunur, çapraz kilitlenme (deadlock) olmaz.
        var wanted = dto.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new StockItemDto { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .OrderBy(i => i.ProductId)
            .ToList();

        var existing = await FindAsync(dto.OrderId, ct);
        if (existing is not null) return new ReserveResult(ReserveOutcome.Replayed, ToDto(existing, replayed: true));

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1) Önce rezervasyon satırı: OrderId tekil dizini "bu siparişi ilk kim aldı?" sorusunu veritabanına yaptırır.
        //    Aynı orderId ile eşzamanlı ikinci istek burada ilkinin bitmesini bekler, sonra tekil ihlali alır.
        var now = DateTime.UtcNow;
        var reservation = new StockReservation
        {
            Id = Guid.CreateVersion7(),
            OrderId = dto.OrderId,
            Status = ReservationStatus.Reserved,
            CreatedAt = now,
            Items = wanted.Select(w => new StockReservationItem { ProductId = w.ProductId, Quantity = w.Quantity }).ToList(),
        };
        db.StockReservations.Add(reservation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var winner = await FindAsync(dto.OrderId, ct);
            if (winner is not null) return new ReserveResult(ReserveOutcome.Replayed, ToDto(winner, replayed: true));
            throw;
        }

        // 2) Koşullu düşüm: "stok yeterliyse düş" TEK ifadede. Oku-sonra-yaz yok, bu yüzden iki istek aynı son
        //    stoğu iki kez satamaz; satır kilidi ikincisini bekletir ve koşulu güncel stokla yeniden değerlendirir.
        foreach (var item in wanted)
        {
            var rows = await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Products SET Stock = Stock - {item.Quantity}, UpdatedAt = {now} WHERE Id = {item.ProductId} AND IsActive = 1 AND Stock >= {item.Quantity}", ct);
            if (rows == 1) continue;

            // Hepsi ya da hiçbiri: bir kalem yetmediyse bu transaction'daki her şey (rezervasyon + önceki düşümler) geri alınır.
            var conflict = await DiagnoseAsync(item, ct);
            await tx.RollbackAsync(CancellationToken.None);
            return new ReserveResult(ReserveOutcome.Conflict, Conflict: conflict, Error: "Stok yetersiz ya da ürün satışta değil.");
        }

        await tx.CommitAsync(ct);
        await cache.InvalidateAsync(wanted.Select(w => w.ProductId), ct);
        return new ReserveResult(ReserveOutcome.Reserved, ToDto(reservation, replayed: false));
    }

    public async Task<OperationResult<ReservationResultDto>> ReleaseAsync(Guid orderId, CancellationToken ct)
    {
        if (orderId == Guid.Empty) return OperationResult<ReservationResultDto>.Validation("orderId boş olamaz.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // "Sahiplenme": durumu Reserved -> Released yapan TEK çağrı stoğu geri koyar. Aynı anda gelen ikinci release
        // 0 satır etkiler ve stok eklemez; yani iade en çok bir kez yapılır.
        var now = DateTime.UtcNow;
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE StockReservations SET Status = {nameof(ReservationStatus.Released)}, ReleasedAt = {now} WHERE OrderId = {orderId} AND Status = {nameof(ReservationStatus.Reserved)}", ct);

        var reservation = await FindAsync(orderId, ct);
        if (reservation is null)
        {
            // Bu sipariş için hiç rezervasyon yok (ör. rezerve çağrısı stok yetmediği için reddedildi): zararsız, 200.
            await tx.RollbackAsync(CancellationToken.None);
            return OperationResult<ReservationResultDto>.Ok(new ReservationResultDto { OrderId = orderId, Status = "None", Replayed = true });
        }

        if (claimed == 0)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return OperationResult<ReservationResultDto>.Ok(ToDto(reservation, replayed: true));
        }

        foreach (var item in reservation.Items.OrderBy(i => i.ProductId))
        {
            // Ürün arada silindiyse 0 satır etkilenir; iade edilecek yer kalmamıştır, sorun değil.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Products SET Stock = Stock + {item.Quantity}, UpdatedAt = {now} WHERE Id = {item.ProductId}", ct);
        }

        await tx.CommitAsync(ct);
        await cache.InvalidateAsync(reservation.Items.Select(i => i.ProductId), ct);
        reservation.Status = ReservationStatus.Released;
        return OperationResult<ReservationResultDto>.Ok(ToDto(reservation, replayed: false));
    }

    private Task<StockReservation?> FindAsync(Guid orderId, CancellationToken ct)
        => db.StockReservations.AsNoTracking().Include(r => r.Items).FirstOrDefaultAsync(r => r.OrderId == orderId, ct);

    private async Task<StockConflict> DiagnoseAsync(StockItemDto item, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == item.ProductId)
            .Select(p => new { p.IsActive, p.Stock })
            .FirstOrDefaultAsync(ct);

        return product switch
        {
            null => new StockConflict(item.ProductId, "ProductNotFound", item.Quantity, 0),
            { IsActive: false } => new StockConflict(item.ProductId, "ProductUnavailable", item.Quantity, 0),
            _ => new StockConflict(item.ProductId, "InsufficientStock", item.Quantity, product.Stock),
        };
    }

    private static ReservationResultDto ToDto(StockReservation r, bool replayed) => new()
    {
        OrderId = r.OrderId,
        Status = r.Status.ToString(),
        Replayed = replayed,
        Items = r.Items.OrderBy(i => i.ProductId).Select(i => new StockItemDto { ProductId = i.ProductId, Quantity = i.Quantity }).ToList(),
    };

    private static ReserveResult Invalid(string error) => new(ReserveOutcome.Invalid, Error: error);

    // SQL Server: 2601 = tekil dizin ihlali, 2627 = tekil kısıt ihlali.
    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };
}
