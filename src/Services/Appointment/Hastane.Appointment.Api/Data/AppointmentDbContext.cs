using Hastane.Appointment.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hastane.Appointment.Api.Data;

public class AppointmentDbContext : DbContext
{
    public AppointmentDbContext(DbContextOptions<AppointmentDbContext> options) : base(options)
    {
    }

    public DbSet<Entities.Appointment> Appointments => Set<Entities.Appointment>();
    public DbSet<AppointmentDetail> AppointmentDetails => Set<AppointmentDetail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppointmentDbContext).Assembly);
}
