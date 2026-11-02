using backend_stockpilot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace backend_stockpilot.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(c => c.Name).HasColumnType("citext").IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers", t =>
            t.HasCheckConstraint("CK_Suppliers_LeadTimeDays", "\"LeadTimeDays\" BETWEEN 0 AND 180"));

        builder.Property(s => s.Name).HasMaxLength(150).IsRequired();
        builder.Property(s => s.ContactPerson).HasMaxLength(150);
        builder.Property(s => s.Email).HasMaxLength(254);
        builder.Property(s => s.Phone).HasMaxLength(40);
        builder.Property(s => s.Address).HasMaxLength(300);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        builder.HasIndex(s => s.IsActive);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", t =>
        {
            t.HasCheckConstraint("CK_Products_CostPrice", "\"CostPrice\" >= 0");
            t.HasCheckConstraint("CK_Products_SellingPrice", "\"SellingPrice\" >= 0");
            t.HasCheckConstraint("CK_Products_ReorderLevel", "\"ReorderLevel\" >= 0");
        });

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();

        builder.Property(p => p.Sku).HasColumnType("citext").IsRequired();
        builder.HasIndex(p => p.Sku).IsUnique();

        builder.Property(p => p.Barcode).HasMaxLength(64);
        builder.HasIndex(p => p.Barcode).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");

        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.ImageUrl).HasMaxLength(500);
        builder.Property(p => p.CostPrice).HasPrecision(14, 2);
        builder.Property(p => p.SellingPrice).HasPrecision(14, 2);

        builder.HasIndex(p => new { p.IsActive, p.ArchivedAt });

        builder.HasOne(p => p.Category).WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(p => p.Supplier).WithMany()
            .HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.SetNull);
    }
}