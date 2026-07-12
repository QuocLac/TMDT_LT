using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TMDT_LT.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutFinancialSnapshotsAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Shipping",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CodAmount",
                table: "Shipping",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Shipping",
                type: "datetime",
                nullable: false,
                defaultValueSql: "(getdate())");

            migrationBuilder.AddColumn<decimal>(
                name: "InsuranceValue",
                table: "Shipping",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "Shipping",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastWebhookAt",
                table: "Shipping",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderCode",
                table: "Shipping",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderStatus",
                table: "Shipping",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServiceTypeId",
                table: "Shipping",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingFee",
                table: "Shipping",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Shipping",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppliedVoucherCode",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutIdempotencyKey",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GrandTotalAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ShippingDistrictId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingFee",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingProvinceId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingWard",
                table: "Orders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingWardCode",
                table: "Orders",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SubtotalAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CheckoutAttempts",
                columns: table => new
                {
                    CheckoutAttemptId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    SelectedAddressId = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutAttempts", x => x.CheckoutAttemptId);
                });

            migrationBuilder.CreateTable(
                name: "ShippingEvents",
                columns: table => new
                {
                    ShippingEventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShippingId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    TrackingNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderStatus = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    MappedStatus = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingEvents", x => x.ShippingEventId);
                    table.ForeignKey(
                        name: "FK_ShippingEvents_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "OrderId");
                    table.ForeignKey(
                        name: "FK_ShippingEvents_Shipping_ShippingId",
                        column: x => x.ShippingId,
                        principalTable: "Shipping",
                        principalColumn: "ShippingId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shipping_ProviderCode_TrackingNumber",
                table: "Shipping",
                columns: new[] { "ProviderCode", "TrackingNumber" },
                unique: true,
                filter: "[ProviderCode] IS NOT NULL AND [TrackingNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CheckoutIdempotencyKey",
                table: "Orders",
                column: "CheckoutIdempotencyKey",
                unique: true,
                filter: "[CheckoutIdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutAttempts_CustomerId_CreatedAt",
                table: "CheckoutAttempts",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutAttempts_IdempotencyKey",
                table: "CheckoutAttempts",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutAttempts_OrderId",
                table: "CheckoutAttempts",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutAttempts_Status_ExpiresAt",
                table: "CheckoutAttempts",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingEvents_IdempotencyKey",
                table: "ShippingEvents",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShippingEvents_OrderId_ReceivedAt",
                table: "ShippingEvents",
                columns: new[] { "OrderId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingEvents_ShippingId_ReceivedAt",
                table: "ShippingEvents",
                columns: new[] { "ShippingId", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutAttempts");

            migrationBuilder.DropTable(
                name: "ShippingEvents");

            migrationBuilder.DropIndex(
                name: "IX_Shipping_ProviderCode_TrackingNumber",
                table: "Shipping");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CheckoutIdempotencyKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "CodAmount",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "InsuranceValue",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "LastWebhookAt",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "ProviderCode",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "ProviderStatus",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "ServiceTypeId",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "ShippingFee",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Shipping");

            migrationBuilder.DropColumn(
                name: "AppliedVoucherCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CheckoutIdempotencyKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "GrandTotalAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingDistrictId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingFee",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingProvinceId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingWard",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingWardCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SubtotalAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "Orders");
        }
    }
}
