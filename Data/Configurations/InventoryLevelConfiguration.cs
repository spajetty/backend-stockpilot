using backend_stockpilot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace backend_stockpilot.Data.Configurations;

public class InventoryLevelConfiguration : IEntityTypeConfiguration<InventoryLevel>
{
    public void Configure(EntityTypeBuilder<InventoryLevel> builder)
    {
        builder.ToTable("InventoryLevels", t =>
        {
            t.HasCheckConstraint("CK_InventoryLevels_Quantity", "\"QuantityOnHand\" >= 0");
            t.HasCheckConstraint("CK_InventoryLevels_ReorderOverride",
                "\"ReorderLevelOverride\" IS NULL OR \"ReorderLevelOverride\" >= 0");
        });

        builder.HasKey(i => new { i.StoreId, i.ProductId });

        builder.HasOne(i => i.Store).WithMany()
            .HasForeignKey(i => i.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Product).WithMany()
            .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.ProductId);
    }
}

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements", t =>
        {
            t.HasCheckConstraint("CK_StockMovements_Change", "\"QuantityChange\" <> 0");

            t.HasCheckConstraint("CK_StockMovements_Math",
                "(\"QuantityBefore\" IS NULL AND \"QuantityAfter\" IS NULL) OR " +
                "(\"QuantityBefore\" IS NOT NULL AND \"QuantityAfter\" IS NOT NULL " +
                "AND \"QuantityBefore\" + \"QuantityChange\" = \"QuantityAfter\" AND \"QuantityAfter\" >= 0)");
        });

        builder.Property(m => m.MovementType).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Approval).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.UnitCost).HasPrecision(14, 2);
        builder.Property(m => m.Reason).HasMaxLength(500);
        builder.Property(m => m.Reference).HasMaxLength(100);

        builder.HasOne(m => m.Store).WithMany()
            .HasForeignKey(m => m.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Product).WithMany()
            .HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => new { m.ProductId, m.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(m => new { m.StoreId, m.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(m => new { m.StoreId, m.Approval }).HasFilter("\"Approval\" = 'Pending'");
    }
}