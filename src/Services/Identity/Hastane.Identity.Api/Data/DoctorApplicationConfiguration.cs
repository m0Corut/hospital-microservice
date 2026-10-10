using Hastane.Identity.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hastane.Identity.Api.Data;

public class DoctorApplicationConfiguration : IEntityTypeConfiguration<DoctorApplication>
{
    public void Configure(EntityTypeBuilder<DoctorApplication> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserId).IsRequired().HasMaxLength(450);
        builder.Property(a => a.BranchId).HasMaxLength(64);
        builder.Property(a => a.LicenseNumber).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Note).IsRequired().HasMaxLength(500);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.CreatedAt).HasColumnType("datetime2");
        builder.Property(a => a.DecidedAt).HasColumnType("datetime2");
        builder.Property(a => a.DecidedBy).HasMaxLength(450);
        builder.Property(a => a.DecisionNote).HasMaxLength(500);

        // Kullanıcı başına tek başvuru; kullanıcı silinirse başvurusu da gider.
        builder.HasIndex(a => a.UserId).IsUnique();
        builder.HasIndex(a => new { a.Status, a.CreatedAt });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
