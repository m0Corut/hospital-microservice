using Hastane.Payment.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Payment.Api.Data;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(20).IsUnicode(false);
        builder.HasIndex(i => i.InvoiceNumber, "UX_Invoices_InvoiceNumber").IsUnique();

        builder.Property(i => i.PatientId).IsRequired().HasMaxLength(64);
        builder.Property(i => i.DoctorId).IsRequired().HasMaxLength(64);
        builder.Property(i => i.Currency).IsRequired().HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(i => i.IssuedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();

        builder.Property(i => i.Subtotal).HasPrecision(18, 2);
        builder.Property(i => i.TaxPercent).HasPrecision(18, 2);
        builder.Property(i => i.TaxAmount).HasPrecision(18, 2);
        builder.Property(i => i.Total).HasPrecision(18, 2);

        builder.HasIndex(i => new { i.PatientId, i.IssuedAt });
        builder.HasIndex(i => new { i.DoctorId, i.IssuedAt });

        // Bire-bir: bir ödemenin en çok bir faturası olur (PaymentId tekil). Fatura yasal belge: ödeme satırı
        // silinmeye kalkarsa fatura da sessizce yok olmasın diye cascade DEĞİL, Restrict.
        builder.HasOne(i => i.Payment)
            .WithOne(p => p.Invoice)
            .HasForeignKey<Invoice>(i => i.PaymentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // Bire-çok: fatura silinirse kalemleri de gider (kalem tek başına anlamsız).
        builder.HasMany(i => i.Lines)
            .WithOne(l => l.Invoice)
            .HasForeignKey(l => l.InvoiceId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
