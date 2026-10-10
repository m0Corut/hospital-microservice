using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderEntity = Hastane.Order.Api.Entities.Order;

namespace Hastane.Order.Api.Data;

public class OrderConfiguration : IEntityTypeConfiguration<OrderEntity>
{
    public void Configure(EntityTypeBuilder<OrderEntity> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.OrderNumber).IsRequired().HasMaxLength(20);
        builder.Property(o => o.PatientUserId).IsRequired().HasMaxLength(64);
        builder.Property(o => o.PatientName).IsRequired().HasMaxLength(200);
        builder.Property(o => o.ShippingAddress).IsRequired().HasMaxLength(300);
        builder.Property(o => o.Note).HasMaxLength(500);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        // Para: decimal(18,2). double/float ikili kesirdir, 0.1 + 0.2 != 0.3 olur.
        builder.Property(o => o.TotalAmount).HasPrecision(18, 2);
        builder.Property(o => o.Currency).IsRequired().HasMaxLength(3);
        builder.Property(o => o.PaymentMethod).IsRequired().HasMaxLength(30);
        builder.Property(o => o.CancelReason).HasMaxLength(300);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(100);

        builder.Property(o => o.Version).IsConcurrencyToken();

        builder.Property(o => o.CreatedAt).HasColumnType("datetime(6)").HasConversion<UtcDateTimeConverter>();
        builder.Property(o => o.UpdatedAt).HasColumnType("datetime(6)").HasConversion<UtcDateTimeConverter>();

        // Sipariş numarası tekil: sayaç hatalı olsa bile veritabanı mükerrer numarayı reddeder (son savunma).
        builder.HasIndex(o => o.OrderNumber, "UX_Orders_OrderNumber").IsUnique();

        // Çift tıklama savunması: aynı hasta + aynı anahtar ikinci satır açamaz. MariaDB tekil indekste
        // birden çok NULL'a izin verir, yani anahtarsız siparişler birbirini engellemez.
        builder.HasIndex(o => new { o.PatientUserId, o.IdempotencyKey }, "UX_Orders_Patient_IdempotencyKey").IsUnique();

        builder.HasIndex(o => new { o.PatientUserId, o.CreatedAt });
        builder.HasIndex(o => o.Status);

        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
