using Hastane.Pharmacy.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Pharmacy.Api.Data;

public class MedicineConfiguration : IEntityTypeConfiguration<Medicine>
{
    public void Configure(EntityTypeBuilder<Medicine> builder)
    {
        builder.ToTable("Medicines", t =>
        {
            // Son savunma hattı: koşullu UPDATE'i atlayan bir kod yolu çıksa bile veritabanı eksi stoğu reddeder.
            t.HasCheckConstraint("CK_Medicines_Stock_NonNegative", "[Stock] >= 0");
            t.HasCheckConstraint("CK_Medicines_MinStock_NonNegative", "[MinStock] >= 0");
        });
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever(); // Guid v7'yi uygulama üretir

        builder.Property(m => m.Name).IsRequired().HasMaxLength(150);
        builder.Property(m => m.NormalizedName).IsRequired().HasMaxLength(150);
        builder.Property(m => m.ActiveIngredient).IsRequired().HasMaxLength(150);
        builder.Property(m => m.NormalizedIngredient).IsRequired().HasMaxLength(150);
        builder.Property(m => m.Form).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(m => m.Strength).IsRequired().HasMaxLength(50);
        builder.Property(m => m.Barcode).IsRequired().HasMaxLength(20).IsUnicode(false);
        builder.Property(m => m.Price).HasPrecision(18, 2);

        builder.Property(m => m.CreatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(m => m.UpdatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();

        // Eşzamanlılık belirteci: UPDATE ... WHERE Id = @id AND RowVersion = @eski. Satır arada değiştiyse 0 satır etkilenir.
        builder.Property(m => m.RowVersion).IsRowVersion();

        builder.HasIndex(m => m.Barcode, "UX_Medicines_Barcode").IsUnique();
        builder.HasIndex(m => m.NormalizedName, "IX_Medicines_NormalizedName");
        builder.HasIndex(m => new { m.IsActive, m.Stock });
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(s => s.Reason).IsRequired().HasMaxLength(200);
        builder.Property(s => s.CreatedBy).HasMaxLength(64);
        builder.Property(s => s.CreatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();

        builder.HasIndex(s => new { s.MedicineId, s.CreatedAt });
        builder.HasIndex(s => s.PrescriptionId);

        // Hareket defteri silinmez: ilaç satırı silinmeye kalkarsa (yumuşak silme yüzünden olmaz) veritabanı engellesin.
        builder.HasOne(s => s.Medicine)
            .WithMany()
            .HasForeignKey(s => s.MedicineId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class DispensationConfiguration : IEntityTypeConfiguration<Dispensation>
{
    public void Configure(EntityTypeBuilder<Dispensation> builder)
    {
        builder.ToTable("Dispensations");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.PatientId).IsRequired().HasMaxLength(64);
        builder.Property(d => d.DoctorId).IsRequired().HasMaxLength(64);
        builder.Property(d => d.DispensedBy).HasMaxLength(64);
        builder.Property(d => d.DispensedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();

        // "Aynı reçete iki kez karşılanmaz" kuralının GERÇEK garantisi: iki istek aynı anda gelse bile ikincisi bu dizinde patlar.
        builder.HasIndex(d => d.PrescriptionId, "UX_Dispensations_PrescriptionId").IsUnique();
        builder.HasIndex(d => d.DispensedAt);

        builder.HasMany(d => d.Lines)
            .WithOne(l => l.Dispensation)
            .HasForeignKey(l => l.DispensationId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DispensationLineConfiguration : IEntityTypeConfiguration<DispensationLine>
{
    public void Configure(EntityTypeBuilder<DispensationLine> builder)
    {
        builder.ToTable("DispensationLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.PrescriptionMedicineName).IsRequired().HasMaxLength(200);
        builder.Property(l => l.MedicineName).IsRequired().HasMaxLength(150);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 2);

        builder.HasIndex(l => l.MedicineId);
    }
}
