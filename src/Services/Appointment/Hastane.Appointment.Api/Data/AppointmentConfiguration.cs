using Hastane.Appointment.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Appointment.Api.Data;

public class AppointmentConfiguration : IEntityTypeConfiguration<Entities.Appointment>
{
    public void Configure(EntityTypeBuilder<Entities.Appointment> builder)
    {
        builder.ToTable("Appointments");
        builder.HasKey(a => a.Id);
        // Guid'i uygulama (Guid v7) üretir; EF ya da SQL Server yeni değer üretmeye çalışmasın.
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.DoctorId).IsRequired().HasMaxLength(64);
        builder.Property(a => a.PatientId).IsRequired().HasMaxLength(64);
        builder.Property(a => a.BranchId).IsRequired().HasMaxLength(64);

        builder.Property(a => a.Note).HasMaxLength(500);

        builder.Property(a => a.AppointmentDate).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();
        builder.Property(a => a.CreatedAt).HasColumnType("datetime2").HasConversion<UtcDateTimeConverter>();

        // Enum veritabanında metin: "Pending", "Confirmed"...
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Filtreler (doktor + tarih aralığı, hasta) bu indekslerle taranır.
        builder.HasIndex(a => new { a.DoctorId, a.AppointmentDate });
        builder.HasIndex(a => a.PatientId);

        // Son savunma: aynı doktorun aynı dakikada iki AKTİF randevusu olamaz. Filtre, iptal edilmiş satırları
        // indeksin dışında bırakır; iptal edilen slot yeniden alınabilir. Servis kontrolü iki isteğin aynı anda
        // geçtiği yarış durumunda yetmez, bu indeks ikincisini reddeder.
        builder.HasIndex(a => new { a.DoctorId, a.AppointmentDate }, "UX_Appointments_Doctor_Slot_Active")
            .IsUnique()
            .HasFilter("[Status] <> N'Cancelled'");

        // Bire-bir: bir randevunun en çok bir detayı olur; randevu silinince detay da silinir (cascade).
        builder.HasOne(a => a.Detail)
            .WithOne(d => d.Appointment)
            .HasForeignKey<AppointmentDetail>(d => d.AppointmentId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
