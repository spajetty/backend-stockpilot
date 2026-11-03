using Microsoft.EntityFrameworkCore;
using backend_stockpilot.Models;
using backend_stockpilot.Services.Interfaces;

namespace backend_stockpilot.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentStore? currentStore = null)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryLevel> InventoryLevels => Set<InventoryLevel>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<RefundItem> RefundItems => Set<RefundItem>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    private int? CurrentStoreId => currentStore?.StoreId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(255).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.MiddleName).HasMaxLength(100);
            e.Property(u => u.Surname).HasMaxLength(100);
            e.Property(u => u.Gender).HasConversion<string>().HasMaxLength(20);
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(30);
            e.Property(u => u.GoogleSubject).HasMaxLength(64);
            e.HasIndex(u => u.GoogleSubject).IsUnique();
        });

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.HasPostgresExtension("citext");

        ApplyStoreFilters(modelBuilder);
    }

    private void ApplyStoreFilters(ModelBuilder mb)
    {
        mb.Entity<InventoryLevel>().HasQueryFilter(e => CurrentStoreId == null || e.StoreId == CurrentStoreId);
        mb.Entity<StockMovement>().HasQueryFilter(e => CurrentStoreId == null || e.StoreId == CurrentStoreId);
        mb.Entity<Sale>().HasQueryFilter(e => CurrentStoreId == null || e.StoreId == CurrentStoreId);
        mb.Entity<Refund>().HasQueryFilter(e => CurrentStoreId == null || e.StoreId == CurrentStoreId);
        mb.Entity<PurchaseOrder>().HasQueryFilter(e => CurrentStoreId == null || e.StoreId == CurrentStoreId);
        mb.Entity<Payment>().HasQueryFilter(e => CurrentStoreId == null || e.StoreId == CurrentStoreId);
        mb.Entity<AuditLog>().HasQueryFilter(e => CurrentStoreId == null || e.StoreId == CurrentStoreId);

        mb.Entity<SaleItem>().HasQueryFilter(e => CurrentStoreId == null || e.Sale.StoreId == CurrentStoreId);
        mb.Entity<RefundItem>().HasQueryFilter(e => CurrentStoreId == null || e.Refund.StoreId == CurrentStoreId);
        mb.Entity<PurchaseOrderItem>().HasQueryFilter(e => CurrentStoreId == null || e.PurchaseOrder.StoreId == CurrentStoreId);
    }
}