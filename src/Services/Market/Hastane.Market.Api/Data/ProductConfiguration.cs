using Hastane.Market.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Market.Api.Data;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // Stok eksiye düşemez: uygulama koşullu UPDATE ile korur, bu kural son savunma olarak veritabanında da durur.
        builder.ToTable("Products", t => t.HasCheckConstraint("CK_Products_Stock_NonNegative", "[Stock] >= 0"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).IsRequired().HasMaxLength(120);
        builder.Property(p => p.Description).IsRequired().HasMaxLength(2000);
        builder.Property(p => p.Category).IsRequired().HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(p => p.Price).HasPrecision(18, 2);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(p => p.ImageUrl).HasMaxLength(300);
        builder.Property(p => p.CreatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(p => p.UpdatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => new { p.IsActive, p.Category, p.Name });
    }
}

public class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Status).IsRequired().HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        builder.Property(r => r.CreatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(r => r.ReleasedAt).HasColumnType("datetime2");

        // Aynı sipariş ikinci kez rezerve edilemez: idempotency'nin veritabanı düzeyindeki garantisi.
        builder.HasIndex(r => r.OrderId, "UX_StockReservations_OrderId").IsUnique();

        builder.HasMany(r => r.Items)
            .WithOne()
            .HasForeignKey(i => i.ReservationId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockReservationItemConfiguration : IEntityTypeConfiguration<StockReservationItem>
{
    public void Configure(EntityTypeBuilder<StockReservationItem> builder)
    {
        builder.ToTable("StockReservationItems");
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => i.ProductId);
    }
}
