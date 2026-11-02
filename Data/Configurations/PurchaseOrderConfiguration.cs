using backend_stockpilot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace backend_stockpilot.Data.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.Property(p => p.PoNumber).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(p => p.Source).HasConversion<string>().HasMaxLength(30);

        builder.HasOne(p => p.Store).WithMany()
            .HasForeignKey(p => p.StoreId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.StoreId, p.PoNumber }).IsUnique();
        builder.HasIndex(p => new { p.StoreId, p.Status });
    }
}

public class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> builder)
    {
        builder.ToTable("PurchaseOrderItems", t =>
        {
            t.HasCheckConstraint("CK_PurchaseOrderItems_Ordered", "\"QuantityOrdered\" > 0");
            t.HasCheckConstraint("CK_PurchaseOrderItems_Received",
                "\"QuantityReceived\" >= 0 AND \"QuantityReceived\" <= \"QuantityOrdered\"");
            t.HasCheckConstraint("CK_PurchaseOrderItems_UnitCost", "\"UnitCost\" >= 0");
        });

        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.UnitCost).HasPrecision(14, 2);

        builder.HasOne(i => i.PurchaseOrder).WithMany(p => p.Items)
            .HasForeignKey(i => i.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.Product).WithMany()
            .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}