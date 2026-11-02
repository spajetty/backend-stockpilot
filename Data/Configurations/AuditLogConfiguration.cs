using backend_stockpilot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace backend_stockpilot.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.ActorName).HasMaxLength(150).IsRequired();
        builder.Property(a => a.ActorRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Entity).HasMaxLength(50).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(64);
        builder.Property(a => a.Summary).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(a => a.Store).WithMany()
            .HasForeignKey(a => a.StoreId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.StoreId, a.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(a => new { a.Entity, a.EntityId });
    }
}