using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using TMDT_LT.Models;

namespace TMDT_LT.Data;

public partial class ApplicationDbContext : DbContext
{
    public ApplicationDbContext()
    {
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    public DbSet<UserBehaviorLog> UserBehaviorLogs { get; set; }
    public virtual DbSet<AnalyticsSession> AnalyticsSessions { get; set; }
    public virtual DbSet<AnalyticsEvent> AnalyticsEvents { get; set; }
    public virtual DbSet<SearchQueryLog> SearchQueryLogs { get; set; }
    public virtual DbSet<Account> Account { get; set; }

    public virtual DbSet<Address> Address { get; set; }

    public virtual DbSet<Brands> Brands { get; set; }

    public virtual DbSet<CartItems> CartItems { get; set; }

    public virtual DbSet<Categories> Categories { get; set; }

    public virtual DbSet<Customer> Customer { get; set; }

    public virtual DbSet<FavoriteDetails> FavoriteDetails { get; set; }

    public virtual DbSet<Favorites> Favorites { get; set; }

    public virtual DbSet<InventoryTransactions> InventoryTransactions { get; set; }

    public virtual DbSet<Notifications> Notifications { get; set; }

    public virtual DbSet<OrderDetails> OrderDetails { get; set; }

    public virtual DbSet<Orders> Orders { get; set; }

    public virtual DbSet<Payments> Payments { get; set; }

    public virtual DbSet<ProductVariants> ProductVariants { get; set; }

    public virtual DbSet<Products> Products { get; set; }

    public virtual DbSet<PurchaseOrderDetails> PurchaseOrderDetails { get; set; }

    public virtual DbSet<PurchaseOrders> PurchaseOrders { get; set; }

    public virtual DbSet<ReviewDetails> ReviewDetails { get; set; }

    public virtual DbSet<Reviews> Reviews { get; set; }

    public virtual DbSet<Shipping> Shipping { get; set; }

    public virtual DbSet<Suppliers> Suppliers { get; set; }

    public virtual DbSet<ProductImages> ProductImages { get; set; }

    public virtual DbSet<OrderHistory> OrderHistories { get; set; }

    public virtual DbSet<ShippingCarriers> ShippingCarriers { get; set; }

    public virtual DbSet<CampaignBanners> CampaignBanners { get; set; }

    public virtual DbSet<Promotions> Promotions { get; set; }

    public virtual DbSet<PromotionRules> PromotionRules { get; set; }

    public virtual DbSet<CustomerWallet> CustomerWallet { get; set; }

    public virtual DbSet<Blog> Blogs { get; set; }

    public virtual DbSet<BlogMedia> BlogMedias { get; set; }

    public virtual DbSet<OrderReturns> OrderReturns { get; set; }

    // =========================================================
    // WMS & SALES ORDER ENGINE (KHO, LÔ HÀNG, ĐA CỬA HÀNG)
    // =========================================================
    public DbSet<InventoryLots> InventoryLots { get; set; }
    public DbSet<ProductSerials> ProductSerials { get; set; }
    public virtual DbSet<Stores> Stores { get; set; }
    public virtual DbSet<SalesOrders> SalesOrders { get; set; }
    public virtual DbSet<SalesOrderDetails> SalesOrderDetails { get; set; }
    // =========================================================
    // MARKETING ENGINE (CAMPAIGNS & FLASH SALES)
    // =========================================================
    public virtual DbSet<Campaigns> Campaigns { get; set; }
    public virtual DbSet<CampaignRules> CampaignRules { get; set; }
    public virtual DbSet<FlashSales> FlashSales { get; set; }
    public virtual DbSet<FlashSaleItems> FlashSaleItems { get; set; }
    public virtual DbSet<FlashSaleChangeLogs> FlashSaleChangeLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ==========================================
        // CẤU HÌNH MODULE QUẢN LÝ KHO & LÔ HÀNG (WMS)
        // ==========================================
        modelBuilder.Entity<InventoryLots>()
            .Property(p => p.UnitCost).HasColumnType("decimal(18,2)");

        modelBuilder.Entity<InventoryLots>().ToTable("InventoryLots");
        modelBuilder.Entity<ProductSerials>().ToTable("ProductSerials");
        modelBuilder.Entity<InventoryLots>().HasKey(l => l.LotId);
        modelBuilder.Entity<ProductSerials>().HasKey(s => s.SerialId);

        // Chặn vòng lặp xóa tự động (Cascade Path) trên bảng IMEI Serials
        modelBuilder.Entity<ProductSerials>()
            .HasOne(s => s.Variant)
            .WithMany()
            .HasForeignKey(s => s.VariantId)
            .OnDelete(DeleteBehavior.Restrict);

        // ==========================================
        // CẤU HÌNH NÂNG CẤP ĐA CỬA HÀNG & PHÂN PHỐI (SO)
        // ==========================================
        modelBuilder.Entity<Stores>(entity =>
        {
            entity.HasKey(e => e.StoreId);
            entity.ToTable("Stores");
        });

        modelBuilder.Entity<SalesOrders>(entity =>
        {
            entity.HasKey(e => e.SOId);
            entity.ToTable("SalesOrders");
            entity.Property(p => p.COGSTotal).HasColumnType("decimal(18,2)");
            entity.Property(p => p.DiscountAmount).HasColumnType("decimal(18,2)");
            entity.Property(p => p.DiscountPercent).HasColumnType("decimal(18,2)");
            entity.Property(p => p.ProfitTotal).HasColumnType("decimal(18,2)");
            entity.Property(p => p.ShippingCost).HasColumnType("decimal(18,2)");
            entity.Property(p => p.TaxAmount).HasColumnType("decimal(18,2)");
            entity.Property(p => p.TotalAmount).HasColumnType("decimal(18,2)");

            entity.HasOne(d => d.Store)
                .WithMany(p => p.SalesOrders)
                .HasForeignKey(d => d.StoreId);
        });

        modelBuilder.Entity<SalesOrderDetails>(entity =>
        {
            entity.HasKey(e => e.SODetailId);
            entity.ToTable("SalesOrderDetails");
            entity.Property(p => p.Profit).HasColumnType("decimal(18,2)");
            entity.Property(p => p.TaxRate).HasColumnType("decimal(18,2)");
            entity.Property(p => p.TotalAmount).HasColumnType("decimal(18,2)");
            entity.Property(p => p.TotalCost).HasColumnType("decimal(18,2)");
            entity.Property(p => p.UnitCost).HasColumnType("decimal(18,2)");
            entity.Property(p => p.UnitPrice).HasColumnType("decimal(18,2)");

            entity.HasOne(d => d.SalesOrder)
                .WithMany(p => p.SalesOrderDetails)
                .HasForeignKey(d => d.SOId)
                .OnDelete(DeleteBehavior.Cascade);

            // Restrict chặn cycles hoặc multiple cascade paths trong SQL Server
            entity.HasOne(d => d.Variant)
                .WithMany()
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.Lot)
                .WithMany()
                .HasForeignKey(d => d.LotId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ==========================================
        // HỆ THỐNG CŨ RÀNG BUỘC CƠ SỞ DỮ LIỆU SẴN CÓ
        // ==========================================
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.AccountId).HasName("PK__ACCOUNT__349DA586ED2C4A8B");

            entity.ToTable("ACCOUNT");

            entity.HasIndex(e => e.Email, "UQ__ACCOUNT__A9D105343636F462").IsUnique();

            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Password)
                .HasMaxLength(255)
                .IsUnicode(false);
            entity.Property(e => e.Phone).HasMaxLength(15);
            entity.Property(e => e.Role)
                .HasMaxLength(20)
                .HasDefaultValue("Customer");
        });

        modelBuilder.Entity<Address>(entity =>
        {
            entity.HasKey(e => e.AddressId).HasName("PK__ADDRESS__091C2A1B1C6BE152");

            entity.ToTable("ADDRESS");

            entity.Property(e => e.AddressId).HasColumnName("AddressID");
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.Country)
                .HasMaxLength(100)
                .HasDefaultValue("Việt Nam");
            entity.Property(e => e.CustomerId).HasColumnName("CustomerID");
            entity.Property(e => e.District).HasMaxLength(100);
            entity.Property(e => e.IsDefault).HasDefaultValue(false);
            entity.Property(e => e.PostalCode).HasMaxLength(20);
            entity.Property(e => e.Street).HasMaxLength(255);

            entity.HasOne(d => d.Customer).WithMany(p => p.Address)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("FK__ADDRESS__Custome__4CA06362");
        });

        modelBuilder.Entity<Brands>(entity =>
        {
            entity.HasKey(e => e.BrandId).HasName("PK__Brands__DAD4F05EC0CD1933");

            entity.Property(e => e.BrandName).HasMaxLength(100);
        });

        modelBuilder.Entity<CartItems>(entity =>
        {
            entity.HasKey(e => e.CartItemId).HasName("PK__CartItem__488B0B0A6AA25EFB");

            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CustomerId).HasColumnName("CustomerID");
            entity.Property(e => e.Quantity).HasDefaultValue(1);

            entity.HasOne(d => d.Customer).WithMany(p => p.CartItems)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__CartItems__Custo__628FA481");

            entity.HasOne(d => d.Variant).WithMany(p => p.CartItems)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__CartItems__Varia__6383C8BA");
        });

        modelBuilder.Entity<Categories>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PK__Categori__19093A0B2267D68E");

            entity.Property(e => e.CategoryName).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(255);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(e => e.CustomerId).HasName("PK__CUSTOMER__A4AE64B8E5F479AD");

            entity.ToTable("CUSTOMER");

            entity.Property(e => e.CustomerId).HasColumnName("CustomerID");
            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.CustomerType)
                .HasMaxLength(20)
                .HasDefaultValue("Thường");
            entity.Property(e => e.FullName).HasMaxLength(100);
            entity.Property(e => e.Gender)
                .HasMaxLength(10)
                .HasDefaultValue("Nam");
            entity.Property(e => e.Phone).HasMaxLength(15);

            entity.HasOne(d => d.Account).WithMany(p => p.Customer)
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("FK__CUSTOMER__Accoun__47DBAE45");
        });

        modelBuilder.Entity<FavoriteDetails>(entity =>
        {
            entity.HasKey(e => e.FavoriteDetailId).HasName("PK__Favorite__722A629CCA1DDD69");

            entity.HasOne(d => d.Favorite).WithMany(p => p.FavoriteDetails)
                .HasForeignKey(d => d.FavoriteId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__FavoriteD__Favor__6A30C649");

            entity.HasOne(d => d.Variant).WithMany(p => p.FavoriteDetails)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__FavoriteD__Varia__6B24EA82");
        });

        modelBuilder.Entity<Favorites>(entity =>
        {
            entity.HasKey(e => e.FavoriteId).HasName("PK__Favorite__CE74FAD561DCBFBC");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CustomerId).HasColumnName("CustomerID");

            entity.HasOne(d => d.Customer).WithMany(p => p.Favorites)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__Favorites__Custo__6754599E");
        });

        modelBuilder.Entity<InventoryTransactions>(entity =>
        {
            entity.HasKey(e => e.TransactionId).HasName("PK__Inventor__55433A6B9B679ADF");

            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.Note).HasMaxLength(255);
            entity.Property(e => e.TransactionDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.TransactionType).HasMaxLength(50);

            entity.HasOne(d => d.Account).WithMany(p => p.InventoryTransactions)
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("FK__Inventory__Accou__0F624AF8");

            entity.HasOne(d => d.Variant).WithMany(p => p.InventoryTransactions)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__Inventory__Varia__0E6E26BF");
        });

        modelBuilder.Entity<Notifications>(entity =>
        {
            entity.HasKey(e => e.NotificationId).HasName("PK__Notifica__20CF2E12DC6C59C4");

            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.IsRead).HasDefaultValue(false);
            entity.Property(e => e.Message).HasMaxLength(255);
            entity.Property(e => e.Type).HasMaxLength(50);

            entity.HasOne(d => d.Account).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__Notificat__Accou__5165187F");
        });

        modelBuilder.Entity<OrderDetails>(entity =>
        {
            entity.HasKey(e => e.OrderDetailId).HasName("PK__OrderDet__D3B9D36CF7E78EDE");

            entity.Property(e => e.UnitPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.IsFlashSaleItem).HasDefaultValue(false);

            entity.HasOne(d => d.Order).WithMany(p => p.OrderDetails)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__OrderDeta__Order__72C60C4A");

            entity.HasOne(d => d.Variant).WithMany(p => p.OrderDetails)
                .HasForeignKey(d => d.VariantId)
                .HasConstraintName("FK__OrderDeta__Varia__73BA3083");

            entity.HasOne(d => d.FlashSaleItem).WithMany(p => p.OrderDetails)
                .HasForeignKey(d => d.FlashSaleItemId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_OrderDetails_FlashSaleItems_FlashSaleItemId");
        });

        modelBuilder.Entity<Orders>(entity =>
        {
            entity.HasKey(e => e.OrderId).HasName("PK__Orders__C3905BCF9B226E60");

            entity.Property(e => e.CustomerId).HasColumnName("CustomerID");
            entity.Property(e => e.OrderDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.ShippingCity).HasMaxLength(100);
            entity.Property(e => e.ShippingCountry)
                .HasMaxLength(100)
                .HasDefaultValue("Việt Nam");
            entity.Property(e => e.ShippingDistrict).HasMaxLength(100);
            entity.Property(e => e.ShippingFullName).HasMaxLength(100);
            entity.Property(e => e.ShippingPhone).HasMaxLength(15);
            entity.Property(e => e.ShippingStreet).HasMaxLength(255);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.IsStockDeducted).HasDefaultValue(false);
            entity.Property(e => e.StockDeductedAt).HasColumnType("datetime");

            entity.HasOne(d => d.Customer).WithMany(p => p.Orders)
                .HasForeignKey(d => d.CustomerId)
                .HasConstraintName("FK__Orders__Customer__6FE99F9F");
        });

        modelBuilder.Entity<Payments>(entity =>
        {
            entity.HasKey(e => e.PaymentId).HasName("PK__Payments__9B556A38278EFDA0");

            entity.Property(e => e.PaymentDate).HasColumnType("datetime");
            entity.Property(e => e.PaymentMethod).HasMaxLength(50);
            entity.Property(e => e.PaymentStatus).HasMaxLength(50);

            entity.HasOne(d => d.Order).WithMany(p => p.Payments)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__Payments__OrderI__76969D2E");
        });

        modelBuilder.Entity<ProductVariants>(entity =>
        {
            entity.HasKey(e => e.VariantId).HasName("PK__ProductV__0EA233841FB75659");

            entity.Property(e => e.Color).HasMaxLength(30);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.DiscountPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.ImageUrl).HasMaxLength(255);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Price).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Ram)
                .HasMaxLength(20)
                .HasColumnName("RAM");
            entity.Property(e => e.Stock).HasDefaultValue(0);
            entity.Property(e => e.Storage).HasMaxLength(20);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");

            entity.HasOne(d => d.Product).WithMany(p => p.ProductVariants)
                .HasForeignKey(d => d.ProductId)
                .HasConstraintName("FK__ProductVa__Produ__5DCAEF64");
        });

        modelBuilder.Entity<Products>(entity =>
        {
            entity.HasKey(e => e.ProductId).HasName("PK__Products__B40CC6CDD4296AC2");

            entity.Property(e => e.ChargerIncluded).HasDefaultValue(true);
            entity.Property(e => e.Chipset).HasMaxLength(50);
            entity.Property(e => e.CreatedDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Dimensions).HasMaxLength(50);
            entity.Property(e => e.FrontCamera).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.MainImage).HasMaxLength(255);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.OperatingSystem).HasMaxLength(30);
            entity.Property(e => e.RearCamera).HasMaxLength(100);
            entity.Property(e => e.ScreenSize).HasColumnType("decimal(4, 2)");
            entity.Property(e => e.ScreenTech).HasMaxLength(40);
            entity.Property(e => e.UpdatedDate).HasColumnType("datetime");
            entity.Property(e => e.Weight).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.Brand).WithMany(p => p.Products)
                .HasForeignKey(d => d.BrandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Products__BrandI__571DF1D5");

            entity.HasOne(d => d.Category).WithMany(p => p.Products)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Products__Catego__5812160E");
        });

        modelBuilder.Entity<PurchaseOrderDetails>(entity =>
        {
            entity.HasKey(e => e.PodetailId).HasName("PK__Purchase__4EB47B3E1D5ABACC");

            entity.Property(e => e.PodetailId).HasColumnName("PODetailId");
            entity.Property(e => e.ImportPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Poid).HasColumnName("POId");

            entity.HasOne(d => d.Po).WithMany(p => p.PurchaseOrderDetails)
                .HasForeignKey(d => d.Poid)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__PurchaseOr__POId__09A971A2");

            entity.HasOne(d => d.Variant).WithMany(p => p.PurchaseOrderDetails)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PurchaseO__Varia__0A9D95DB");
        });

        modelBuilder.Entity<PurchaseOrders>(entity =>
        {
            entity.HasKey(e => e.Poid).HasName("PK__Purchase__5F02A2D4F4830972");

            entity.Property(e => e.Poid).HasColumnName("POId");
            entity.Property(e => e.AccountId).HasColumnName("AccountID");
            entity.Property(e => e.OrderDate)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValue("Hoàn thành");
            entity.Property(e => e.TotalAmount).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.Account).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.AccountId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PurchaseO__Accou__06CD04F7");

            entity.HasOne(d => d.Supplier).WithMany(p => p.PurchaseOrders)
                .HasForeignKey(d => d.SupplierId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PurchaseO__Suppl__05D8E0BE");
        });

        modelBuilder.Entity<ReviewDetails>(entity =>
        {
            entity.HasKey(e => e.ReviewDetailId).HasName("PK__ReviewDe__9B1B82ABC94030C4");

            entity.HasOne(d => d.Review).WithMany(p => p.ReviewDetails)
                .HasForeignKey(d => d.ReviewId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__ReviewDet__Revie__00200768");

            entity.HasOne(d => d.Variant).WithMany(p => p.ReviewDetails)
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__ReviewDet__Varia__01142BA1");
        });

        modelBuilder.Entity<Reviews>(entity =>
        {
            entity.HasKey(e => e.ReviewId).HasName("PK__Reviews__74BC79CE3F461CAA");

            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.CustomerId).HasColumnName("CustomerID");

            entity.HasOne(d => d.Customer).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.CustomerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__Reviews__Custome__7D439ABD");
        });

        modelBuilder.Entity<Shipping>(entity =>
        {
            entity.HasKey(e => e.ShippingId).HasName("PK__Shipping__5FACD5800AF7BF6F");

            entity.Property(e => e.Carrier).HasMaxLength(100);
            entity.Property(e => e.DeliveredDate).HasColumnType("datetime");
            entity.Property(e => e.EstimatedDelivery).HasColumnType("datetime");
            entity.Property(e => e.ShippedDate).HasColumnType("datetime");
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.TrackingNumber).HasMaxLength(100);

            entity.HasOne(d => d.Order).WithMany(p => p.Shipping)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__Shipping__OrderI__797309D9");
        });

        modelBuilder.Entity<Suppliers>(entity =>
        {
            entity.HasKey(e => e.SupplierId).HasName("PK__Supplier__4BE666B4CD56E1BE");

            entity.Property(e => e.ContactName).HasMaxLength(100);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Phone).HasMaxLength(15);
            entity.Property(e => e.SupplierName).HasMaxLength(255);
            entity.Property(e => e.TaxCode)
                .HasMaxLength(50)
                .IsUnicode(false);
        });


        modelBuilder.Entity<FlashSales>(entity =>
        {
            entity.HasKey(e => e.FlashSaleId);
            entity.Property(e => e.Name).HasMaxLength(255);
        });

        modelBuilder.Entity<FlashSaleItems>(entity =>
        {
            entity.HasKey(e => e.ItemId);
            entity.Property(e => e.FlashSalePrice).HasColumnType("decimal(18, 2)");

            entity.HasOne(d => d.FlashSale).WithMany(p => p.FlashSaleItems)
                .HasForeignKey(d => d.FlashSaleId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Variant).WithMany()
                .HasForeignKey(d => d.VariantId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        modelBuilder.Entity<FlashSaleChangeLogs>(entity =>
        {
            entity.HasKey(e => e.ChangeLogId);
            entity.Property(e => e.ActionType).HasMaxLength(80);
            entity.Property(e => e.FieldName).HasMaxLength(100);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime");

            entity.HasOne(d => d.FlashSale).WithMany(p => p.ChangeLogs)
                .HasForeignKey(d => d.FlashSaleId)
                // Audit log là dữ liệu lịch sử: không cascade delete để tránh multiple cascade paths
                // và tránh mất dấu vết chỉnh sửa khi FlashSale bị thao tác nhầm.
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_FlashSaleChangeLogs_FlashSales_FlashSaleId");

            entity.HasOne(d => d.FlashSaleItem).WithMany(p => p.ChangeLogs)
                .HasForeignKey(d => d.ItemId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_FlashSaleChangeLogs_FlashSaleItems_ItemId");

            entity.HasOne(d => d.AdminAccount).WithMany()
                .HasForeignKey(d => d.AdminAccountId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_FlashSaleChangeLogs_ACCOUNT_AdminAccountId");
        });


        modelBuilder.Entity<AnalyticsSession>(entity =>
        {
            entity.HasKey(e => e.AnalyticsSessionId);
            entity.HasIndex(e => e.SessionKey).IsUnique();
            entity.HasIndex(e => e.VisitorKey);
            entity.HasIndex(e => e.CustomerId);
            entity.Property(e => e.SessionKey).HasMaxLength(80).IsRequired();
            entity.Property(e => e.VisitorKey).HasMaxLength(80);
            entity.Property(e => e.LandingPage).HasMaxLength(600);
            entity.Property(e => e.Referrer).HasMaxLength(600);
            entity.Property(e => e.UserAgent).HasMaxLength(600);
            entity.Property(e => e.IpHash).HasMaxLength(128);
            entity.Property(e => e.Source).HasMaxLength(120);
            entity.Property(e => e.Medium).HasMaxLength(120);
            entity.Property(e => e.Campaign).HasMaxLength(120);
            entity.Property(e => e.FirstSeenAt).HasColumnType("datetime");
            entity.Property(e => e.LastSeenAt).HasColumnType("datetime");
            entity.Property(e => e.TotalRevenue).HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.HasIndex(e => e.SessionKey);
            entity.HasIndex(e => e.VisitorKey);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.EventName);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.OrderId);
            entity.Property(e => e.SessionKey).HasMaxLength(80).IsRequired();
            entity.Property(e => e.VisitorKey).HasMaxLength(80);
            entity.Property(e => e.EventName).HasMaxLength(80).IsRequired();
            entity.Property(e => e.SearchKeyword).HasMaxLength(300);
            entity.Property(e => e.PagePath).HasMaxLength(600);
            entity.Property(e => e.Referrer).HasMaxLength(600);
            entity.Property(e => e.EventValue).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
        });

        modelBuilder.Entity<SearchQueryLog>(entity =>
        {
            entity.HasKey(e => e.SearchQueryLogId);
            entity.HasIndex(e => e.NormalizedKeyword);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.SessionKey);
            entity.Property(e => e.SessionKey).HasMaxLength(80);
            entity.Property(e => e.VisitorKey).HasMaxLength(80);
            entity.Property(e => e.Keyword).HasMaxLength(300).IsRequired();
            entity.Property(e => e.NormalizedKeyword).HasMaxLength(300).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime");
        });

        // ==========================================
        // SEED DATA HỆ THỐNG MỚI (MIGRATION SEEDING)
        // ==========================================
        modelBuilder.Entity<Stores>().HasData(
            new Stores
            {
                StoreId = 1,
                StoreCode = "ST001",
                StoreName = "KingPhone Store HCM - Quận 1",
                StoreType = "Direct",
                TaxCode = "0304050607",
                Address = "Quận 1, TP. Hồ Chí Minh",
                Phone = "0283999111",
                Email = "hcm@kingphone.vn",
                ContactPerson = "Nguyễn Văn Trưởng Chi Nhánh",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1)
            },
            new Stores
            {
                StoreId = 2,
                StoreCode = "ST002",
                StoreName = "KingPhone Store HN - Hoàn Kiếm",
                StoreType = "Direct",
                TaxCode = "0304050608",
                Address = "Quận Hoàn Kiếm, Hà Nội",
                Phone = "0243999222",
                Email = "hn@kingphone.vn",
                ContactPerson = "Trần Thị Quản Lý",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1)
            },
            new Stores
            {
                StoreId = 3,
                StoreCode = "AG001",
                StoreName = "Đại lý Ủy quyền Bình Thạnh",
                StoreType = "Agent",
                TaxCode = "0304050609",
                Address = "Quận Bình Thạnh, TP. HCM",
                Phone = "0903888999",
                Email = "binhthanh@gmail.com",
                ContactPerson = "Lê Văn Đối Tác",
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1)
            }
        );

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
