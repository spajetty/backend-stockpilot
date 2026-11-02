using backend_stockpilot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace backend_stockpilot.Data.Configurations;

public class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.ToTable("Stores", t =>
        {
            t.HasCheckConstraint("CK_Stores_TaxRate", "\"TaxRate\" >= 0 AND \"TaxRate\" <= 100");
            t.HasCheckConstraint("CK_Stores_DefaultReorderLevel", "\"DefaultReorderLevel\" >= 0");
        });

        builder.Property(s => s.Code).HasMaxLength(10).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();

        builder.Property(s => s.Name).HasMaxLength(150).IsRequired();
        builder.Property(s => s.Email).HasMaxLength(254);
        builder.Property(s => s.Phone).HasMaxLength(40);
        builder.Property(s => s.Address).HasMaxLength(300);
        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();
        builder.Property(s => s.TaxRate).HasPrecision(5, 2);
        builder.Property(s => s.ReceiptFooter).HasMaxLength(500);
        builder.Property(s => s.TimeZone).HasMaxLength(64).IsRequired();

        builder.HasMany(s => s.Users)
            .WithOne(u => u.Store)
            .HasForeignKey(u => u.StoreId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}