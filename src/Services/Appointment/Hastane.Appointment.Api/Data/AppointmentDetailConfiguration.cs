using Hastane.Appointment.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Appointment.Api.Data;

public class AppointmentDetailConfiguration : IEntityTypeConfiguration<AppointmentDetail>
{
    public void Configure(EntityTypeBuilder<AppointmentDetail> builder)
    {
        builder.ToTable("AppointmentDetails");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Complaint).IsRequired().HasMaxLength(1000);
        builder.Property(d => d.Diagnosis).HasMaxLength(1000);
        builder.Property(d => d.DoctorNotes).HasMaxLength(2000);
        builder.Property(d => d.UpdatedAt).HasColumnType("datetime2")
            .HasConversion(
                v => v == null ? (DateTime?)null : UtcDateTimeConverter.Normalize(v.Value),
                v => v == null ? (DateTime?)null : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

        // Tekil indeks bire-bir kuralını veritabanı seviyesinde zorlar: aynı randevuya ikinci detay eklenemez.
        builder.HasIndex(d => d.AppointmentId).IsUnique();
    }
}
