using Hastane.Prescription.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Prescription.Api.Data;

public class PrescriptionDbContext : DbContext
{
    public PrescriptionDbContext(DbContextOptions<PrescriptionDbContext> options) : base(options)
    {
    }

    public DbSet<Entities.Prescription> Prescriptions => Set<Entities.Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrescriptionDbContext).Assembly);
}
