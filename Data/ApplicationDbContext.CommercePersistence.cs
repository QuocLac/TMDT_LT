using Microsoft.EntityFrameworkCore;
using TMDT_LT.Models;

namespace TMDT_LT.Data;

public partial class ApplicationDbContext
{
    public virtual DbSet<PaymentTransactions> PaymentTransactions { get; set; }

    public virtual DbSet<OrderReservations> OrderReservations { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderDetails>(entity =>
        {
            entity.Property(e => e.ProductNameSnapshot).HasMaxLength(200).HasDefaultValue(string.Empty);
            entity.Property(e => e.VariantCodeSnapshot).HasMaxLength(80).HasDefaultValue(string.Empty);
            entity.Property(e => e.VariantNameSnapshot).HasMaxLength(200).HasDefaultValue(string.Empty);
            entity.Property(e => e.ImageUrlSnapshot).HasMaxLength(500).HasDefaultValue(string.Empty);
            entity.Property(e => e.OriginalUnitPrice).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.DiscountAmountPerUnit).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.LineTotal).HasColumnType("decimal(18, 2)");
        });

        modelBuilder.Entity<Payments>(entity =>
        {
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Currency).HasMaxLength(10).HasDefaultValue("VND");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(100);
            entity.Property(e => e.LastResponseCode).HasMaxLength(20);
            entity.Property(e => e.LastTransactionStatus).HasMaxLength(20);
            entity.Property(e => e.LastProcessedAt).HasColumnType("datetime");
            entity.Property(e => e.FailureReason).HasMaxLength(500);
        });

        modelBuilder.Entity<ProductVariants>(entity =>
        {
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<PaymentTransactions>(entity =>
        {
            entity.HasKey(e => e.PaymentTransactionId);
            entity.ToTable("PaymentTransactions");

            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
            entity.HasIndex(e => new { e.OrderId, e.ReceivedAt });
            entity.HasIndex(e => new { e.PaymentId, e.ReceivedAt });

            entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ProviderTransactionId).HasMaxLength(100);
            entity.Property(e => e.Amount).HasColumnType("decimal(18, 2)");
            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ResponseCode).HasMaxLength(20);
            entity.Property(e => e.TransactionStatus).HasMaxLength(20);
            entity.Property(e => e.PayloadHash).HasMaxLength(64);
            entity.Property(e => e.ReceivedAt).HasColumnType("datetime");
            entity.Property(e => e.ProcessedAt).HasColumnType("datetime");
            entity.Property(e => e.ErrorMessage).HasMaxLength(500);

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

            entity.HasIndex(e => new { e.OrderId, e.VariantId }).IsUnique();
            entity.HasIndex(e => new { e.Status, e.ExpiresAt });

            entity.Property(e => e.Status).HasMaxLength(30).IsRequired();
            entity.Property(e => e.ReservedAt).HasColumnType("datetime");
            entity.Property(e => e.ExpiresAt).HasColumnType("datetime");
            entity.Property(e => e.ConsumedAt).HasColumnType("datetime");
            entity.Property(e => e.ReleasedAt).HasColumnType("datetime");
            entity.Property(e => e.Reason).HasMaxLength(500);

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
