using Hastane.Patient.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Patient.Api.Data;

public class PatientProfileConfiguration : IEntityTypeConfiguration<PatientProfile>
{
    public void Configure(EntityTypeBuilder<PatientProfile> builder)
    {
        builder.ToTable("PatientProfiles");
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).HasMaxLength(64).ValueGeneratedNever();

        builder.Property(p => p.DateOfBirth).HasColumnType("date");
        // Enum'lar metin olarak saklanır: tablo elle okunabilir, enum sırası değişse veri bozulmaz.
        builder.Property(p => p.Gender).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.BloodType).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Phone).HasMaxLength(20);
        builder.Property(p => p.Address).HasMaxLength(300);
        builder.Property(p => p.City).HasMaxLength(60);
        builder.Property(p => p.EmergencyContactName).HasMaxLength(100);
        builder.Property(p => p.EmergencyContactPhone).HasMaxLength(20);
        builder.Property(p => p.UpdatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
    }
}
