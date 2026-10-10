using Hastane.Market.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Market.Api.Data;

public class MarketDbContext : DbContext
{
    public MarketDbContext(DbContextOptions<MarketDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<StockReservationItem> StockReservationItems => Set<StockReservationItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(MarketDbContext).Assembly);
}
