using Hastane.Order.Api.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderEntity = Hastane.Order.Api.Entities.Order;

namespace Hastane.Order.Api.Data;

public class OrderDbContext : DbContext
{
    public OrderDbContext(DbContextOptions<OrderDbContext> options) : base(options)
    {
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
