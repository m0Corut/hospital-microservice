using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Prescription.Api.Data;

public class PrescriptionConfiguration : IEntityTypeConfiguration<Entities.Prescription>
{
    public void Configure(EntityTypeBuilder<Entities.Prescription> builder)
    {
        builder.ToTable("Prescriptions");
        builder.HasKey(p => p.Id);
        // Guid'i uygulama (Guid v7) üretir; EF ya da SQL Server yeni değer üretmeye çalışmasın.
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.AppointmentId).IsRequired();
        builder.Property(p => p.DoctorId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.PatientId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.IssuedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(p => p.Notes).HasMaxLength(2000);

        // Liste filtreleri (hasta, doktor, randevu) bu indekslerle taranır.
        builder.HasIndex(p => p.PatientId);
        builder.HasIndex(p => p.DoctorId);
        builder.HasIndex(p => p.AppointmentId);

        // Bire-çok: reçete silinince kalemleri de silinir (ON DELETE CASCADE).
        builder.HasMany(p => p.Items)
            .WithOne(i => i.Prescription)
            .HasForeignKey(i => i.PrescriptionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
