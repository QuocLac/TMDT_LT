using Microsoft.EntityFrameworkCore;
using TMDT_LT.Models;

namespace TMDT_LT.Data;

public partial class ApplicationDbContext
{
    public DbSet<Warehouses> Warehouses { get; set; } = null!;

    public DbSet<OrderInventoryAllocations> OrderInventoryAllocations { get; set; } = null!;

    private static void ConfigureInventoryPhaseB(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Warehouses>(entity =>
        {
            entity.ToTable("Warehouses");
            entity.HasKey(item => item.WarehouseId);
            entity.Property(item => item.WarehouseCode)
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(item => item.WarehouseName)
                .HasMaxLength(150)
                .IsRequired();
            entity.Property(item => item.Address)
                .HasMaxLength(300);
            entity.HasIndex(item => item.WarehouseCode)
                .IsUnique();

            entity.HasData(
                new Warehouses
                {
                    WarehouseId = 1,
                    WarehouseCode = "WH-HCM",
                    WarehouseName = "Kho HCM - Cơ sở chính Quận 1",
                    Address = "Quận 1, TP. Hồ Chí Minh",
                    IsPrimary = true,
                    IsActive = true,
                    CreatedAt = new DateTime(2026, 1, 1)
                },
                new Warehouses
                {
                    WarehouseId = 2,
                    WarehouseCode = "WH-HN",
                    WarehouseName = "Kho HN - Cơ sở chính Hoàn Kiếm",
                    Address = "Quận Hoàn Kiếm, Hà Nội",
                    IsPrimary = false,
                    IsActive = true,
                    CreatedAt = new DateTime(2026, 1, 1)
                });
        });

        modelBuilder.Entity<InventoryLots>(entity =>
        {
            entity.Property(item => item.WarehouseId)
                .HasDefaultValue(1);
            entity.HasIndex(item => new
            {
                item.WarehouseId,
                item.VariantId,
                item.ReceivedDate,
                item.LotId
            });
            entity.HasOne(item => item.Warehouse)
                .WithMany(item => item.InventoryLots)
                .HasForeignKey(item => item.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrders>(entity =>
        {
            entity.Property(item => item.WarehouseId)
                .HasDefaultValue(1);
            entity.Property(item => item.InvoiceNumber)
                .HasMaxLength(100);
            entity.Property(item => item.GoodsSubtotal)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.InputVatAmount)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.InboundShippingFee)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.OtherCost)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.InventoryCapitalizedCost)
                .HasColumnType("decimal(18,2)");
            entity.HasOne(item => item.Warehouse)
                .WithMany(item => item.PurchaseOrders)
                .HasForeignKey(item => item.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrderDetails>(entity =>
        {
            entity.Property(item => item.TaxRate)
                .HasColumnType("decimal(9,6)");
            entity.Property(item => item.InputVatAmount)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.AllocatedInboundCost)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.LandedUnitCost)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.CapitalizedLineCost)
                .HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<Orders>(entity =>
        {
            entity.Property(item => item.MerchandiseNetRevenueAmount)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.CogsAmount)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.GrossProfitAmount)
                .HasColumnType("decimal(18,2)");
            entity.HasOne(item => item.FulfillmentWarehouse)
                .WithMany(item => item.FulfillmentOrders)
                .HasForeignKey(item => item.FulfillmentWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => item.FulfillmentWarehouseId);
        });


        modelBuilder.Entity<SalesOrders>(entity =>
        {
            entity.HasOne(item => item.FromWarehouse)
                .WithMany(item => item.DistributionOrders)
                .HasForeignKey(item => item.FromWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new
            {
                item.FromWarehouseId,
                item.OrderDate
            });
        });

        modelBuilder.Entity<OrderDetails>(entity =>
        {
            entity.Property(item => item.NetRevenueAmount)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.CogsAmount)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.GrossProfitAmount)
                .HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<InventoryTransactions>(entity =>
        {
            entity.Property(item => item.UnitCostSnapshot)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.TotalCostSnapshot)
                .HasColumnType("decimal(18,2)");
            entity.HasOne(item => item.Warehouse)
                .WithMany()
                .HasForeignKey(item => item.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Lot)
                .WithMany()
                .HasForeignKey(item => item.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.WarehouseId, item.TransactionDate });
            entity.HasIndex(item => item.LotId);
        });

        modelBuilder.Entity<OrderInventoryAllocations>(entity =>
        {
            entity.ToTable("OrderInventoryAllocations");
            entity.HasKey(item => item.AllocationId);
            entity.Property(item => item.UnitCost)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.TotalCost)
                .HasColumnType("decimal(18,2)");
            entity.Property(item => item.Status)
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(item => item.Reason)
                .HasMaxLength(500);

            entity.HasOne(item => item.Order)
                .WithMany(item => item.OrderInventoryAllocations)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.OrderDetail)
                .WithMany(item => item.InventoryAllocations)
                .HasForeignKey(item => item.OrderDetailId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Variant)
                .WithMany()
                .HasForeignKey(item => item.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Lot)
                .WithMany(item => item.OrderInventoryAllocations)
                .HasForeignKey(item => item.LotId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Warehouse)
                .WithMany(item => item.OrderInventoryAllocations)
                .HasForeignKey(item => item.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(item => new
            {
                item.OrderId,
                item.Status
            });
            entity.HasIndex(item => new
            {
                item.OrderDetailId,
                item.LotId
            });
            entity.HasIndex(item => new
            {
                item.WarehouseId,
                item.VariantId,
                item.AllocatedAt
            });
        });
    }
}
