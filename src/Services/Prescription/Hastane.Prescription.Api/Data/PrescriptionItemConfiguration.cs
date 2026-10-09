using Hastane.Prescription.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Prescription.Api.Data;

public class PrescriptionItemConfiguration : IEntityTypeConfiguration<PrescriptionItem>
{
    public void Configure(EntityTypeBuilder<PrescriptionItem> builder)
    {
        builder.ToTable("PrescriptionItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.LineNo).IsRequired();
        builder.Property(i => i.MedicineName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Dosage).IsRequired().HasMaxLength(100);
        builder.Property(i => i.Frequency).IsRequired().HasMaxLength(100);
        builder.Property(i => i.DurationDays).IsRequired();
        builder.Property(i => i.Instructions).HasMaxLength(500);

        // Tekil: bir reçetede aynı sıra numarası iki kez olamaz. Başı PrescriptionId olduğu için
        // bir reçetenin kalemlerini bulmak (GET {id}, Count, cascade silme) da bu indeksle hızlanır.
        builder.HasIndex(i => new { i.PrescriptionId, i.LineNo }).IsUnique();
    }
}
