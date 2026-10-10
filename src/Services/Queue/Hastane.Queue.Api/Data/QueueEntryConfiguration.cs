using Hastane.Queue.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Queue.Api.Data;

public class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> builder)
    {
        builder.ToTable("QueueEntries");
        builder.HasKey(e => e.Id);
        // Guid'i uygulama (Guid v7) üretir.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.DoctorId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.PatientUserId).IsRequired().HasMaxLength(64);
        builder.Property(e => e.DisplayName).IsRequired().HasMaxLength(64);
        builder.Property(e => e.QueueDate).HasColumnType("date");

        builder.Property(e => e.CreatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(e => e.CalledAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(e => e.CompletedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Bir randevu en çok bir kez sıraya girer: ikinci check-in (aynı anda gelse de) bu indekste durur.
        builder.HasIndex(e => e.AppointmentId).IsUnique().HasDatabaseName("UX_QueueEntries_Appointment");

        // Günlük numara çakışmasının SON savunması: iki eşzamanlı check-in aynı "MAX+1"i okusa bile biri bu indekste
        // reddedilir ve servis yeni numarayla yeniden dener.
        builder.HasIndex(e => new { e.DoctorId, e.QueueDate, e.Number }).IsUnique().HasDatabaseName("UX_QueueEntries_Doctor_Day_Number");

        // "Bir doktorun aynı anda tek Called kaydı": filtreli tekil indeks. İki eşzamanlı call-next'ten biri reddedilir (409).
        builder.HasIndex(e => new { e.DoctorId, e.QueueDate }, "UX_QueueEntries_Doctor_Day_OneCalled")
            .IsUnique()
            .HasFilter("[Status] = N'Called'");

        builder.HasIndex(e => e.PatientUserId);
    }
}
