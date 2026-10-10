using Hastane.Patient.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Patient.Api.Data;

public class PatientDbContext : DbContext
{
    public PatientDbContext(DbContextOptions<PatientDbContext> options) : base(options)
    {
    }

    public DbSet<PatientProfile> Profiles => Set<PatientProfile>();
    public DbSet<FavoriteDoctor> Favorites => Set<FavoriteDoctor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(PatientDbContext).Assembly);
}
