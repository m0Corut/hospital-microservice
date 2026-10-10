using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hastane.Order.Api.Data;

// YALNIZ `dotnet ef migrations ...` için. Oracle MySQL sağlayıcısı boş bağlantı cümlesini kabul etmez (SQL Server sağlayıcısı eder);
// bağlantı cümlesi user-secrets'ta olduğundan araç uygulamayı başlatıp context'i bulamıyor. Migration üretmek veritabanına
// BAĞLANMAZ, bu yüzden buradaki cümle yer tutucudur ve çalışma zamanında hiç kullanılmaz.
public class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseMySQL("Server=localhost;Port=3307;Database=hastane_order;User=design;Password=design")
            .Options;
        return new OrderDbContext(options);
    }
}
