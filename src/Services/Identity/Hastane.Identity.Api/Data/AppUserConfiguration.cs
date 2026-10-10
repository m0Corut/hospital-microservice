using Hastane.Identity.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Identity.Api.Data;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.CreatedAt).IsRequired().HasColumnType("datetime2");

        // Bir doktor kaydı en çok bir kullanıcıya bağlanır. Filtre: DoctorId'si boş (NULL) olan binlerce hasta
        // "aynı değer" sayılıp çakışmasın; benzersizlik yalnız dolu değerler için aranır.
        builder.Property(u => u.DoctorId).HasMaxLength(64);
        builder.Property(u => u.PhotoFileName).HasMaxLength(100);
        builder.HasIndex(u => u.DoctorId).IsUnique().HasFilter("[DoctorId] IS NOT NULL");
    }
}
