using backend_stockpilot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace backend_stockpilot.Data.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("Sales", t =>
        {
            t.HasCheckConstraint("CK_Sales_Subtotal", "\"Subtotal\" >= 0");
            t.HasCheckConstraint("CK_Sales_Discount", "\"Discount\" >= 0");
            t.HasCheckConstraint("CK_Sales_Tax", "\"Tax\" >= 0");
            t.HasCheckConstraint("CK_Sales_Total", "\"Total\" >= 0");
        });

        builder.Property(s => s.SaleNumber).HasMaxLength(40).IsRequired();
        builder.Property(s => s.CashierName).HasMaxLength(150).IsRequired();
        builder.Property(s => s.VoidReason).HasMaxLength(500);

        builder.Property(s => s.Subtotal).HasPrecision(14, 2);
        builder.Property(s => s.Discount).HasPrecision(14, 2);
        builder.Property(s => s.Tax).HasPrecision(14, 2);
        builder.Property(s => s.Total).HasPrecision(14, 2);
        builder.Property(s => s.CashTendered).HasPrecision(14, 2);
        builder.Property(s => s.ChangeDue).HasPrecision(14, 2);

        builder.Property(s => s.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.PaymentStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(s => s.Store).WithMany()
            .HasForeignKey(s => s.StoreId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.StoreId, s.SaleNumber }).IsUnique();
        builder.HasIndex(s => new { s.StoreId, s.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(s => new { s.CashierId, s.CreatedAt }).IsDescending(false, true);
    }
}

public class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("SaleItems", t =>
        {
            t.HasCheckConstraint("CK_SaleItems_Quantity", "\"Quantity\" > 0");
            t.HasCheckConstraint("CK_SaleItems_UnitPrice", "\"UnitPrice\" >= 0");
            t.HasCheckConstraint("CK_SaleItems_UnitCost", "\"UnitCost\" >= 0");
            t.HasCheckConstraint("CK_SaleItems_LineTotal", "\"LineTotal\" >= 0");
        });

        builder.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Sku).HasMaxLength(100).IsRequired();
        builder.Property(i => i.UnitPrice).HasPrecision(14, 2);
        builder.Property(i => i.UnitCost).HasPrecision(14, 2);
        builder.Property(i => i.LineTotal).HasPrecision(14, 2);

        builder.HasOne(i => i.Sale).WithMany(s => s.Items)
            .HasForeignKey(i => i.SaleId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product).WithMany()
            .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds", t => t.HasCheckConstraint("CK_Refunds_Amount", "\"Amount\" >= 0"));

        builder.Property(r => r.Amount).HasPrecision(14, 2);
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.ProcessedName).HasMaxLength(150).IsRequired();

        builder.HasOne(r => r.Store).WithMany()
            .HasForeignKey(r => r.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Sale).WithMany(s => s.Refunds)
            .HasForeignKey(r => r.SaleId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.StoreId, r.CreatedAt }).IsDescending(false, true);
    }
}

public class RefundItemConfiguration : IEntityTypeConfiguration<RefundItem>
{
    public void Configure(EntityTypeBuilder<RefundItem> builder)
    {
        builder.ToTable("RefundItems", t =>
        {
            t.HasCheckConstraint("CK_RefundItems_Quantity", "\"Quantity\" > 0");
            t.HasCheckConstraint("CK_RefundItems_Amount", "\"Amount\" >= 0");
        });

        builder.Property(i => i.Amount).HasPrecision(14, 2);

        builder.HasOne(i => i.Refund).WithMany(r => r.Items)
            .HasForeignKey(i => i.RefundId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.SaleItem).WithMany()
            .HasForeignKey(i => i.SaleItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Product).WithMany()
            .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}