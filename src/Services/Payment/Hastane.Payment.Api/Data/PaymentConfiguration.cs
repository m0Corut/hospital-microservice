using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Payment.Api.Data;

public class PaymentConfiguration : IEntityTypeConfiguration<Entities.Payment>
{
    public void Configure(EntityTypeBuilder<Entities.Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.PatientId).IsRequired().HasMaxLength(64);
        builder.Property(p => p.DoctorId).IsRequired().HasMaxLength(64);
        // Para: decimal(18,2). double/float ikili kesirdir, 0.1 + 0.2 != 0.3 olur.
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(p => p.CardBrand).HasMaxLength(20);
        builder.Property(p => p.CardLast4).HasMaxLength(4).IsFixedLength().IsUnicode(false);
        builder.Property(p => p.ProviderReference).HasMaxLength(100);
        builder.Property(p => p.FailureReason).HasMaxLength(200);

        // Anahtar büyük/küçük harfe duyarlı karşılaştırılsın ("abc" ile "ABC" ayrı anahtar): ikili (BIN2) harmanlama.
        builder.Property(p => p.IdempotencyKey).IsRequired().HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        builder.Property(p => p.RequestHash).IsRequired().HasMaxLength(64).IsFixedLength().IsUnicode(false);

        builder.Property(p => p.CreatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(p => p.PaidAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(p => p.RefundRequestedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(p => p.FailureKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.RefundedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();

        // Çift tıklama savunması: aynı anahtar ikinci satır açamaz (idempotency'nin veritabanı ayağı).
        builder.HasIndex(p => p.IdempotencyKey, "UX_Payments_IdempotencyKey").IsUnique();

        // Son savunma: bir randevunun en çok BİR Paid ödemesi olur. Filtre iade/başarısız satırları dışarıda bırakır:
        // iade edilen randevu yeniden ödenebilir. Servis kontrolü iki isteğin aynı anda geçtiği yarışta yetmez.
        builder.HasIndex(p => p.AppointmentId, "UX_Payments_Appointment_Paid")
            .IsUnique()
            .HasFilter("[Status] = N'Paid'");

        builder.HasIndex(p => p.PatientId);
        builder.HasIndex(p => p.DoctorId);
        builder.HasIndex(p => p.AppointmentId);
        builder.HasIndex(p => p.CreatedAt);
    }
}
