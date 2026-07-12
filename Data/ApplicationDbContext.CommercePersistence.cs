using Microsoft.EntityFrameworkCore;
using TMDT_LT.Models;

namespace TMDT_LT.Data;

public partial class ApplicationDbContext
{
    public virtual DbSet<PaymentTransactions> PaymentTransactions { get; set; }

    public virtual DbSet<OrderReservations> OrderReservations { get; set; }

    public virtual DbSet<ShippingEvents> ShippingEvents { get; set; }

    public virtual DbSet<CheckoutAttempts> CheckoutAttempts { get; set; }

    public virtual DbSet<OrderInvoiceRequests> OrderInvoiceRequests { get; set; }

    public virtual DbSet<ReturnInspections> ReturnInspections { get; set; }

    public virtual DbSet<ReturnInspectionItems> ReturnInspectionItems { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderDetails>(entity =>
        {
            entity.Property(e => e.ProductNameSnapshot)
                .HasMaxLength(200)
                .HasDefaultValue(string.Empty);
            entity.Property(e => e.VariantCodeSnapshot)
                .HasMaxLength(80)
                .HasDefaultValue(string.Empty);
            entity.Property(e => e.VariantNameSnapshot)
                .HasMaxLength(200)
                .HasDefaultValue(string.Empty);
            entity.Property(e => e.ImageUrlSnapshot)
                .HasMaxLength(500)
                .HasDefaultValue(string.Empty);
            entity.Property(e => e.OriginalUnitPrice)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DiscountAmountPerUnit)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.LineTotal)
                .HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<Payments>(entity =>
        {
            entity.Property(e => e.Amount)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Currency)
                .HasMaxLength(10)
                .HasDefaultValue("VND");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ProviderTransactionId)
                .HasMaxLength(100);
            entity.Property(e => e.LastResponseCode)
                .HasMaxLength(20);
            entity.Property(e => e.LastTransactionStatus)
                .HasMaxLength(20);
            entity.Property(e => e.LastProcessedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.FailureReason)
                .HasMaxLength(500);
        });

        modelBuilder.Entity<ProductVariants>(entity =>
        {
            entity.Property(e => e.CostPrice)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<Promotions>(entity =>
        {
            // Checkout tăng UsedCount theo mô hình optimistic concurrency.
            // Hai request cùng đọc một quota chỉ có một request cập nhật thành công.
            entity.Property(e => e.UsedCount)
                .IsConcurrencyToken();

            // Ngăn claim bằng cấu hình cũ nếu Admin đổi quota hoặc tắt voucher
            // đúng lúc khách đang checkout.
            entity.Property(e => e.UsageLimit)
                .IsConcurrencyToken();
            entity.Property(e => e.IsActive)
                .IsConcurrencyToken();
            entity.Property(e => e.StartDate)
                .IsConcurrencyToken();
            entity.Property(e => e.EndDate)
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<CustomerWallet>(entity =>
        {
            // Một voucher trong ví chỉ được chuyển từ Saved (0) sang Used (1)
            // bởi một checkout duy nhất.
            entity.Property(e => e.Status)
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<Orders>(entity =>
        {
            entity.Property(e => e.ShippingWard)
                .HasMaxLength(100);
            entity.Property(e => e.ShippingWardCode)
                .HasMaxLength(30);
            entity.Property(e => e.ShippingFee)
                .HasColumnType("decimal(18, 2)");

            entity.Property(e => e.SubtotalAmount)
                .HasColumnType("decimal(18, 2)")
                .HasDefaultValue(0m);
            entity.Property(e => e.DiscountAmount)
                .HasColumnType("decimal(18, 2)")
                .HasDefaultValue(0m);
            entity.Property(e => e.TaxAmount)
                .HasColumnType("decimal(18, 2)")
                .HasDefaultValue(0m);
            entity.Property(e => e.GrandTotalAmount)
                .HasColumnType("decimal(18, 2)")
                .HasDefaultValue(0m);
            entity.Property(e => e.AppliedVoucherCode)
                .HasMaxLength(20);
            entity.Property(e => e.CheckoutIdempotencyKey)
                .HasMaxLength(64);

            entity.HasIndex(e => e.CheckoutIdempotencyKey)
                .IsUnique()
                .HasFilter("[CheckoutIdempotencyKey] IS NOT NULL");
        });

        modelBuilder.Entity<CheckoutAttempts>(entity =>
        {
            entity.HasKey(e => e.CheckoutAttemptId);
            entity.ToTable("CheckoutAttempts");

            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique();
            entity.HasIndex(e => new
            {
                e.CustomerId,
                e.CreatedAt
            });
            entity.HasIndex(e => new
            {
                e.Status,
                e.ExpiresAt
            });
            entity.HasIndex(e => e.OrderId);

            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(e => e.RequestHash)
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(e => e.PaymentMethod)
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime2");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("datetime2");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("datetime2");
            entity.Property(e => e.FailureReason)
                .HasMaxLength(500);
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<OrderInvoiceRequests>(entity =>
        {
            entity.HasKey(current =>
                current.InvoiceRequestId);
            entity.ToTable("OrderInvoiceRequests");

            entity.HasIndex(current =>
                current.OrderId)
                .IsUnique();
            entity.HasIndex(current => new
            {
                current.Status,
                current.RequestedAt
            });
            entity.HasIndex(current =>
                current.TaxCode);

            entity.Property(current =>
                    current.BuyerType)
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(current =>
                    current.BuyerName)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(current =>
                    current.TaxCode)
                .HasMaxLength(20);
            entity.Property(current =>
                    current.BuyerAddress)
                .HasMaxLength(500)
                .IsRequired();
            entity.Property(current =>
                    current.BuyerEmail)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(current =>
                    current.BuyerPhone)
                .HasMaxLength(30);
            entity.Property(current =>
                    current.Status)
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(current =>
                    current.RequestedAt)
                .HasColumnType("datetime2");
            entity.Property(current =>
                    current.ReviewedAt)
                .HasColumnType("datetime2");
            entity.Property(current =>
                    current.ReviewedBy)
                .HasMaxLength(120);
            entity.Property(current =>
                    current.AdminNote)
                .HasMaxLength(500);
            entity.Property(current =>
                    current.IssuedAt)
                .HasColumnType("datetime2");
            entity.Property(current =>
                    current.InvoiceNumber)
                .HasMaxLength(100);
            entity.Property(current =>
                    current.InvoiceLookupCode)
                .HasMaxLength(200);
            entity.Property(current =>
                    current.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(current =>
                    current.Order)
                .WithOne()
                .HasForeignKey<OrderInvoiceRequests>(
                    current => current.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReturnInspections>(entity =>
        {
            entity.HasKey(current =>
                current.InspectionId);
            entity.ToTable("ReturnInspections");

            entity.HasIndex(current =>
                current.ReturnId)
                .IsUnique();
            entity.HasIndex(current =>
                current.OrderId);
            entity.HasIndex(current => new
            {
                current.Status,
                current.UpdatedAt
            });

            entity.Property(current =>
                    current.Status)
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(current =>
                    current.Decision)
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(current =>
                    current.OriginalOrderAmount)
                .HasColumnType("decimal(18, 2)");
            entity.Property(current =>
                    current.EligibleRefundAmount)
                .HasColumnType("decimal(18, 2)");
            entity.Property(current =>
                    current.SummaryNote)
                .HasMaxLength(1000);
            entity.Property(current =>
                    current.CreatedAt)
                .HasColumnType("datetime2");
            entity.Property(current =>
                    current.UpdatedAt)
                .HasColumnType("datetime2");
            entity.Property(current =>
                    current.InspectedAt)
                .HasColumnType("datetime2");
            entity.Property(current =>
                    current.InspectedBy)
                .HasMaxLength(120);
            entity.Property(current =>
                    current.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(current =>
                    current.ReturnRequest)
                .WithOne()
                .HasForeignKey<ReturnInspections>(
                    current => current.ReturnId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(current =>
                    current.Order)
                .WithMany()
                .HasForeignKey(current =>
                    current.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ReturnInspectionItems>(entity =>
        {
            entity.HasKey(current =>
                current.InspectionItemId);
            entity.ToTable("ReturnInspectionItems");

            entity.HasIndex(current => new
            {
                current.InspectionId,
                current.OrderDetailId
            })
                .IsUnique();
            entity.HasIndex(current =>
                current.OrderDetailId);

            entity.Property(current =>
                    current.ConditionCode)
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(current =>
                    current.Disposition)
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(current =>
                    current.UnitAmountSnapshot)
                .HasColumnType("decimal(18, 2)");
            entity.Property(current =>
                    current.EligibleAmount)
                .HasColumnType("decimal(18, 2)");
            entity.Property(current =>
                    current.Note)
                .HasMaxLength(500);
            entity.Property(current =>
                    current.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            entity.HasOne(current =>
                    current.Inspection)
                .WithMany(current =>
                    current.Items)
                .HasForeignKey(current =>
                    current.InspectionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(current =>
                    current.OrderDetail)
                .WithMany()
                .HasForeignKey(current =>
                    current.OrderDetailId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Shipping>(entity =>
        {
            entity.HasIndex(e => new
            {
                e.ProviderCode,
                e.TrackingNumber
            })
                .IsUnique()
                .HasFilter(
                    "[ProviderCode] IS NOT NULL "
                    + "AND [TrackingNumber] IS NOT NULL");

            entity.Property(e => e.ProviderCode)
                .HasMaxLength(50);
            entity.Property(e => e.ProviderStatus)
                .HasMaxLength(80);
            entity.Property(e => e.ShippingFee)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CodAmount)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.InsuranceValue)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.CancelledAt)
                .HasColumnType("datetime");
            entity.Property(e => e.CreatedAt)
                .HasColumnType("datetime")
                .HasDefaultValueSql("(getdate())");
            entity.Property(e => e.UpdatedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.LastWebhookAt)
                .HasColumnType("datetime");
            entity.Property(e => e.LastError)
                .HasMaxLength(500);
        });

        modelBuilder.Entity<ShippingEvents>(entity =>
        {
            entity.HasKey(e => e.ShippingEventId);
            entity.ToTable("ShippingEvents");

            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique();
            entity.HasIndex(e => new
            {
                e.OrderId,
                e.ReceivedAt
            });
            entity.HasIndex(e => new
            {
                e.ShippingId,
                e.ReceivedAt
            });

            entity.Property(e => e.Provider)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(e => e.EventType)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(220)
                .IsRequired();
            entity.Property(e => e.TrackingNumber)
                .HasMaxLength(100);
            entity.Property(e => e.ProviderStatus)
                .HasMaxLength(80);
            entity.Property(e => e.MappedStatus)
                .HasMaxLength(80);
            entity.Property(e => e.PayloadHash)
                .HasMaxLength(64);
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(e => e.ReceivedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.ProcessedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.ErrorMessage)
                .HasMaxLength(500);

            entity.HasOne(e => e.Shipping)
                .WithMany(e => e.ShippingEvents)
                .HasForeignKey(e => e.ShippingId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Order)
                .WithMany(e => e.ShippingEvents)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PaymentTransactions>(entity =>
        {
            entity.HasKey(e => e.PaymentTransactionId);
            entity.ToTable("PaymentTransactions");

            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique();
            entity.HasIndex(e => new
            {
                e.OrderId,
                e.ReceivedAt
            });
            entity.HasIndex(e => new
            {
                e.PaymentId,
                e.ReceivedAt
            });

            entity.Property(e => e.Provider)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(e => e.EventType)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(e => e.ProviderTransactionId)
                .HasMaxLength(100);
            entity.Property(e => e.Amount)
                .HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(e => e.ResponseCode)
                .HasMaxLength(20);
            entity.Property(e => e.TransactionStatus)
                .HasMaxLength(20);
            entity.Property(e => e.PayloadHash)
                .HasMaxLength(64);
            entity.Property(e => e.ReceivedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.ProcessedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.ErrorMessage)
                .HasMaxLength(500);

            entity.HasOne(e => e.Payment)
                .WithMany(e => e.PaymentTransactions)
                .HasForeignKey(e => e.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Order)
                .WithMany(e => e.PaymentTransactions)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<OrderReservations>(entity =>
        {
            entity.HasKey(e => e.ReservationId);
            entity.ToTable("OrderReservations");

            entity.HasIndex(e => new
            {
                e.OrderId,
                e.VariantId
            })
                .IsUnique();
            entity.HasIndex(e => new
            {
                e.Status,
                e.ExpiresAt
            });

            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .IsRequired();
            entity.Property(e => e.ReservedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("datetime");
            entity.Property(e => e.ConsumedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.ReleasedAt)
                .HasColumnType("datetime");
            entity.Property(e => e.Reason)
                .HasMaxLength(500);

            entity.HasOne(e => e.Order)
                .WithMany(e => e.OrderReservations)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Variant)
                .WithMany(e => e.OrderReservations)
                .HasForeignKey(e => e.VariantId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
