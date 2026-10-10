using Hastane.Patient.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Patient.Api.Data;

public class FavoriteDoctorConfiguration : IEntityTypeConfiguration<FavoriteDoctor>
{
    public void Configure(EntityTypeBuilder<FavoriteDoctor> builder)
    {
        builder.ToTable("FavoriteDoctors");
        // Bileşik anahtar = tekillik: aynı hasta aynı doktoru iki kez ekleyemez (yarışta bile veritabanı reddeder).
        builder.HasKey(f => new { f.UserId, f.DoctorId });
        builder.Property(f => f.UserId).HasMaxLength(64);
        builder.Property(f => f.DoctorId).HasMaxLength(64);
        builder.Property(f => f.CreatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
    }
}
