using backend_stockpilot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace backend_stockpilot.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", t => t.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" >= 0"));

        builder.Property(p => p.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ProviderOrderId).HasMaxLength(100);
        builder.Property(p => p.ProviderCaptureId).HasMaxLength(100);
        builder.Property(p => p.Amount).HasPrecision(14, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Property(p => p.RawPayload).HasColumnType("jsonb");

        builder.HasOne(p => p.Store).WithMany()
            .HasForeignKey(p => p.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Sale).WithMany()
            .HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.Provider, p.ProviderOrderId })
            .IsUnique()
            .HasFilter("\"ProviderOrderId\" IS NOT NULL");
        builder.HasIndex(p => new { p.StoreId, p.Status });
    }
}