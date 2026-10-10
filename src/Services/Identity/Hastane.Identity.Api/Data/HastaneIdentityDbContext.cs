using Hastane.Identity.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Identity.Api.Data;

// Tablo adları (AspNetUsers, AspNetRoles, AspNetUserRoles...) Identity'nin varsayılanı kalır.
public class HastaneIdentityDbContext : IdentityDbContext<AppUser, IdentityRole, string>
{
    public HastaneIdentityDbContext(DbContextOptions<HastaneIdentityDbContext> options) : base(options) { }

    public DbSet<DoctorApplication> DoctorApplications => Set<DoctorApplication>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Önce Identity kendi tablolarını kursun, sonra bizim eklemeler üstüne yazılsın.
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(HastaneIdentityDbContext).Assembly);
    }
}
