using Hastane.Pharmacy.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Pharmacy.Api.Data;

public class PharmacyDbContext : DbContext
{
    public PharmacyDbContext(DbContextOptions<PharmacyDbContext> options) : base(options)
    {
    }

    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Dispensation> Dispensations => Set<Dispensation>();
    public DbSet<DispensationLine> DispensationLines => Set<DispensationLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(PharmacyDbContext).Assembly);
}
