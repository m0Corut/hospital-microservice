using Hastane.Queue.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Queue.Api.Data;

public class QueueDbContext : DbContext
{
    public QueueDbContext(DbContextOptions<QueueDbContext> options) : base(options)
    {
    }

    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(QueueDbContext).Assembly);
}
