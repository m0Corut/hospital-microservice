using Hastane.Order.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Order.Api.Services;

// Sipariş numarası: SIP-2026-000123. Sıra kısmı yıl başına tek satırlık sayaç tablosundan (OrderCounters) gelir.
//
// Neden "MAX(sıra)+1" değil: iki istek aynı anda MAX'ı okur, ikisi de aynı sayıyı üretir. Burada tek bir
// "UPDATE ... SET LastValue = LastValue + 1" var: InnoDB satırı kilitler, ikinci işlem birincinin COMMIT'ini bekler,
// sonra artmış değeri görür. SELECT de aynı işlemde olduğu için kendi artırdığımız değeri okur.
// Sayaç satırı işlemden ÖNCE ayrı (autocommit) bir INSERT IGNORE ile açılır: aynı satırda "önce paylaşımlı, sonra
// özel kilit" isteyen işlemler (INSERT-duplicate sonra UPDATE) birbirini kilitler (deadlock); böylece o durum hiç oluşmaz.
public static class OrderNumberGenerator
{
    public static string Format(int year, int sequence) => $"SIP-{year}-{sequence:D6}";

    public static async Task EnsureCounterRowAsync(OrderDbContext db, int year, CancellationToken ct)
        => await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT IGNORE INTO OrderCounters (`Year`, `LastValue`) VALUES ({year}, 0)", ct);

    // MUTLAKA açık bir işlem (BeginTransaction) içinde çağrılır: kilit COMMIT'e kadar tutulur.
    public static async Task<string> NextAsync(OrderDbContext db, int year, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE OrderCounters SET `LastValue` = `LastValue` + 1 WHERE `Year` = {year}", ct);

        var value = await db.Database
            .SqlQuery<int>($"SELECT `LastValue` AS `Value` FROM OrderCounters WHERE `Year` = {year}")
            .SingleAsync(ct);

        return Format(year, value);
    }
}
