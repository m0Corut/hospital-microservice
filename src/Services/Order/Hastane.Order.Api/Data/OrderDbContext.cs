using Hastane.Order.Api.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderEntity = Hastane.Order.Api.Entities.Order;

namespace Hastane.Order.Api.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
    {
        // ÖĞRETİM: EF Core, SaveChanges'i "tek komut grubu" sayarsa işlem (transaction) AÇMADAN gönderebilir. Oracle MySQL
        // sağlayıcısı iptaldeki UPDATE Orders + INSERT OutboxState + INSERT OutboxMessage grubunu işlemsiz gönderiyordu: her
        // ifade ayrı ayrı otomatik commit olur. Sonuç: outbox teslim servisi araya girip mesajsız OutboxState'i görüp siliyor
        // (FK hatası, 500); daha kötüsü "sipariş iptal edildi ama olay yazılmadı" mümkün. Always: her SaveChanges tek işlem,
        // olay iş verisiyle birlikte ya yazılır ya hiç yazılmaz (outbox'ın atomiklik sözü).
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
    }

    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderCounter> OrderCounters => Set<OrderCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);

        // MassTransit outbox tabloları (OutboxMessage, OutboxState, InboxState): olay siparişle aynı işlemde bu tabloya yazılır.
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
